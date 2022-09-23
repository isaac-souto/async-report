using NotificationHub.Model;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using System.Text.Json;

namespace NotificationHub.Workers
{
    public class NotificationWorker : BackgroundService
    {
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
            _model.ExchangeDeclare(_rabbitNotificationExchange, "direct", true, false, null);
            _model.QueueDeclare(_rabbitNotificationQueue, true, false, false, null);
            _model.QueueBind(_rabbitNotificationQueue, _rabbitNotificationExchange, string.Empty, null);

            _model.BasicQos(0, ushort.Parse(_rabbitQOS), false);

            AsyncEventingBasicConsumer consumer = new(_model);

            consumer.Received += Receive;

            _model.BasicConsume(_rabbitNotificationQueue, false, consumer);

            while (!stoppingToken.IsCancellationRequested) await Task.Delay(1000, stoppingToken);

            _model.Dispose();
        }

        private async Task Receive(object sender, BasicDeliverEventArgs eventArgs)
        {
            try
            {
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
