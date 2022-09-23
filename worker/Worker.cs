using Bogus;
using Minio;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ReportWorker.Models;
using System.Text;
using System.Text.Json;

namespace ReportWorker
{
    public class Worker : BackgroundService
    {
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
            ConfigureExchanges();

            AsyncEventingBasicConsumer consumer = new(_model);

            consumer.Received += Receive;

            _model.BasicConsume(_rabbitReportQueue, false, consumer);

            while (!stoppingToken.IsCancellationRequested) await Task.Delay(1000, stoppingToken);

            _model.Dispose();
        }

        private void ConfigureExchanges()
        {
            _model.ExchangeDeclare(_rabbitReportExchange, "direct", true, false, null);
            _model.QueueDeclare(_rabbitReportQueue, true, false, false, null);
            _model.QueueBind(_rabbitReportQueue, _rabbitReportExchange, string.Empty, null);

            _model.ExchangeDeclare(_rabbitNotificationExchange, "direct", true, false, null);
            _model.QueueDeclare(_rabbitNotificationQueue, true, false, false, null);
            _model.QueueBind(_rabbitNotificationQueue, _rabbitNotificationExchange, string.Empty, null);

            _model.BasicQos(0, ushort.Parse(_rabbitQOS), false);

            _model.ConfirmSelect();
        }

        private async Task Receive(object sender, BasicDeliverEventArgs eventArgs)
        {
            try
            {                
                Thread.Sleep(new Random().Next(1000, 5000));

                var Props = _model.CreateBasicProperties();
                Props.DeliveryMode = 2;
                Props.ContentType = "application/json";

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

                _model.BasicPublish(_rabbitNotificationExchange, string.Empty, Props, MessageBytes);
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

            Csv.AppendLine("Nome;Email;Telefone;Endere�o;");

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
    }
}