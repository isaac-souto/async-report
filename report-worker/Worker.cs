using Bogus;
using Minio;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ReportWorker.Helpers;
using ReportWorker.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ReportWorker
{
    public class Worker : BackgroundService
    {
        static readonly ActivitySource Activity = new(nameof(Worker));

        static readonly TextMapPropagator Propagator = new TraceContextPropagator();

        readonly ILogger<Worker> _logger;

        readonly IModel _model;

        readonly MinioClient _minio;

        readonly string _rabbitQOS;

        readonly string _rabbitReportExchange;

        readonly string _rabbitReportQueue;

        readonly string _rabbitNotificationExchange;

        readonly string _rabbitNotificationQueue;

        public Worker(ILogger<Worker> logger, IModel model, MinioClient minio)
        {
            _logger = logger;
            _model = model;
            _minio = minio;
            _rabbitQOS = Environment.GetEnvironmentVariable("RABBITMQ_QOS");
            _rabbitReportExchange = Environment.GetEnvironmentVariable("RABBITMQ_REPORT_EXCHANGE");
            _rabbitReportQueue = Environment.GetEnvironmentVariable("RABBITMQ_REPORT_QUEUE");
            _rabbitNotificationExchange = Environment.GetEnvironmentVariable("RABBITMQ_NOTIFICATION_EXCHANGE");
            _rabbitNotificationQueue = Environment.GetEnvironmentVariable("RABBITMQ_NOTIFICATION_QUEUE");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _model.ConfirmSelect();

            _model.BasicQos(0, ushort.Parse(_rabbitQOS), false);

            ConfigSchema(_rabbitReportExchange, _rabbitReportQueue);
            ConfigSchema(_rabbitNotificationExchange, _rabbitNotificationQueue);

            AsyncEventingBasicConsumer consumer = new(_model);

            consumer.Received += Receive;

            _model.BasicConsume(_rabbitReportQueue, false, consumer);

            while (!stoppingToken.IsCancellationRequested) await Task.Delay(1000, stoppingToken);

            _model.Dispose();
        }

        private void ConfigSchema(string exchangeName, string queueName)
        {
            //Unrouted
            _model.ExchangeDeclare($"{exchangeName}_unrouted", "fanout", true, false);
            _model.QueueDeclare($"{queueName}_unrouted", true, false, false);
            _model.QueueBind($"{queueName}_unrouted", $"{exchangeName}_unrouted", string.Empty);

            //Deadletter
            _model.ExchangeDeclare($"{exchangeName}_deadletter", "fanout", true, false);
            _model.QueueDeclare($"{queueName}_deadletter", true, false, false);
            _model.QueueBind($"{queueName}_deadletter", $"{exchangeName}_deadletter", string.Empty);

            _model.ExchangeDeclare(exchangeName, "direct", true, false, new Dictionary<string, object>() {
                { "alternate-exchange", $"{exchangeName}_unrouted" }
            });
            _model.QueueDeclare(queueName, true, false, false, new Dictionary<string, object>() {
                { "x-dead-letter-exchange", $"{exchangeName}_deadletter" },
                { "alternate-exchange", $"{exchangeName}_unrouted" }
            });
            _model.QueueBind(queueName, exchangeName, string.Empty);
        }

        private async Task Receive(object sender, BasicDeliverEventArgs eventArgs)
        {
            try
            {
                Thread.Sleep(new Random().Next(1000, 5000));

                var ParentContext = Propagator.Extract(default, eventArgs.BasicProperties, ActivityHelper.ExtractTraceContextFromBasicProperties);

                Baggage.Current = ParentContext.Baggage;

                using var activityReport = Activity.StartActivity("Process Message (Report)", ActivityKind.Consumer, ParentContext.ActivityContext);

                var props = _model.CreateBasicProperties();
                props.DeliveryMode = 2;
                props.ContentType = "application/json";

                ActivityHelper.AddActivityTags(activityReport);

                var ReportData = JsonSerializer.Deserialize<ReportModel>(eventArgs.Body.Span);

                var Faker = new Faker("pt_BR");
                var FileName = $"{Faker.Name.FullName()}.csv";
                var FileBytes = GetFile();

                var ObjectUrl = await Upload(FileName, FileBytes);

                var MessageBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new NotificationModel
                {
                    UserId = ReportData.UserId,
                    FileName = FileName,
                    Url = ObjectUrl
                }));

                using var activityNotification = Activity.StartActivity("RabbitMq Publish (Notification)", ActivityKind.Producer);

                AddActivityToHeader(activityNotification, props);

                _model.BasicPublish(_rabbitNotificationExchange, string.Empty, props, MessageBytes);
                _model.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));

                _model.BasicAck(eventArgs.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogInformation("{log}", ex.ToString());
                _model.BasicNack(eventArgs.DeliveryTag, false, false);
            }
        }

        private static byte[] GetFile()
        {
            var ClientFaker = new Faker<ClientModel>("pt_BR")
                .RuleFor(c => c.Name, f => f.Name.FullName(Bogus.DataSets.Name.Gender.Female))
                .RuleFor(c => c.Email, f => f.Internet.Email(f.Person.FirstName).ToLower())
                .RuleFor(c => c.Phone, f => f.Person.Phone)
                .RuleFor(c => c.Address, f => f.Address.StreetAddress());

            IEnumerable<ClientModel> Clients = ClientFaker.Generate(new Random().Next(10, 1000));

            var Csv = new StringBuilder();

            Csv.AppendLine("Nome;Email;Telefone;Endereço;");

            foreach (var client in Clients)
                Csv.AppendLine($"{client.Name};{client.Email};{client.Phone};{client.Address};");

            return Encoding.UTF8.GetBytes(Csv.ToString());
        }

        private async Task<string> Upload(string fileName, byte[] file)
        {
            bool BucketExists = await _minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(Environment.GetEnvironmentVariable("MINIO_BUCKET_FILES")));

            if (!BucketExists) await _minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(Environment.GetEnvironmentVariable("MINIO_BUCKET_FILES")));

            using var filestream = new MemoryStream(file);

            await _minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket(Environment.GetEnvironmentVariable("MINIO_BUCKET_FILES"))
                .WithObject(fileName)
                .WithStreamData(filestream)
                .WithObjectSize(filestream.Length)
                .WithContentType("application/octet-stream"));

            return await _minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
                .WithBucket(Environment.GetEnvironmentVariable("MINIO_BUCKET_FILES"))
                .WithObject(fileName)
                .WithExpiry(60 * 10)
                .WithHeaders(new Dictionary<string, string> {
                    { "response-content-type", "application/octet-stream" }
                }));
        }

        private void AddActivityToHeader(Activity activity, IBasicProperties props)
        {
            Propagator.Inject(new PropagationContext(activity.Context, Baggage.Current), props, InjectContextIntoHeader);
            activity?.SetTag("messaging.system", "rabbitmq");
            activity?.SetTag("messaging.destination_kind", "queue");
            activity?.SetTag("messaging.rabbitmq.queue", "sample");
        }

        private void InjectContextIntoHeader(IBasicProperties props, string key, string value)
        {
            try
            {
                props.Headers ??= new Dictionary<string, object>();
                props.Headers[key] = value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to inject trace context.");
            }
        }
    }
}