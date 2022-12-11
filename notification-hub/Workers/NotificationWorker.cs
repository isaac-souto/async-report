using NotificationHub.Model;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using System.Text.Json;
using OpenTelemetry.Context.Propagation;
using System.Diagnostics;
using OpenTelemetry;
using NotificationHub.Helpers;

namespace NotificationHub.Workers
{
    public class NotificationWorker : BackgroundService
    {
        static readonly ActivitySource Activity = new(nameof(NotificationWorker));

        static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

        readonly ILogger<NotificationWorker> _logger;

        readonly IModel _model;

        readonly IHubContext<Hubs.NotificationHub> _hub;

        readonly string _rabbitQOS;

        readonly string _rabbitNotificationExchange;

        readonly string _rabbitNotificationQueue;

        public NotificationWorker(ILogger<NotificationWorker> logger, IModel model, IHubContext<Hubs.NotificationHub> hub)
        {
            _logger = logger;
            _model = model;
            _hub = hub;
            _rabbitQOS = Environment.GetEnvironmentVariable("RABBITMQ_QOS");
            _rabbitNotificationExchange = Environment.GetEnvironmentVariable("RABBITMQ_NOTIFICATION_EXCHANGE");
            _rabbitNotificationQueue = Environment.GetEnvironmentVariable("RABBITMQ_NOTIFICATION_QUEUE");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {            
            _model.BasicQos(0, ushort.Parse(_rabbitQOS), false);

            ConfigSchema(_rabbitNotificationExchange, _rabbitNotificationQueue);

            AsyncEventingBasicConsumer consumer = new(_model);

            consumer.Received += Receive;

            _model.BasicConsume(_rabbitNotificationQueue, false, consumer);

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
                var ParentContext = Propagator.Extract(default, eventArgs.BasicProperties, ActivityHelper.ExtractTraceContextFromBasicProperties);

                Baggage.Current = ParentContext.Baggage;

                using var activity = Activity.StartActivity("Process Message (Notification)", ActivityKind.Consumer, ParentContext.ActivityContext);

                ActivityHelper.AddActivityTags(activity);

                NotificationModel MessageData = await JsonSerializer.DeserializeAsync<NotificationModel>(new MemoryStream(eventArgs.Body.ToArray()));

                await _hub.Clients.Group(MessageData.UserId.ToString()).SendAsync("notification", MessageData);

                _model.BasicAck(eventArgs.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogInformation("{log}", ex.ToString());
                _model.BasicReject(eventArgs.DeliveryTag, false);
            }
        }
    }
}
