using Microsoft.AspNetCore.Mvc;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using ReportApi.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ReportApi.Controllers
{
    public class ReportController : ControllerBase
    {
        static readonly ActivitySource Activity = new(nameof(ReportController));

        static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

        readonly ILogger<ReportController> _logger;

        public ReportController(ILogger<ReportController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        [Route("api/report")]
        public IActionResult Get()
        {
            return Ok();
        }

        [HttpPost]
        [Route("api/report/{userId:guid}")]
        public IActionResult Post([FromServices] IModel model, Guid userId)
        {
            using var activity = Activity.StartActivity("RabbitMq Publish (Report)", ActivityKind.Producer);

            model.ConfirmSelect();

            ConfigSchema(model, Environment.GetEnvironmentVariable("RABBITMQ_REPORT_EXCHANGE"), Environment.GetEnvironmentVariable("RABBITMQ_REPORT_QUEUE"));

            var props = model.CreateBasicProperties();
            props.DeliveryMode = 2;
            props.ContentType = "application/json";

            var MessageBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new ReportModel
            {
                UserId = userId
            }));

            AddActivityToHeader(activity, props);

            model.BasicPublish(Environment.GetEnvironmentVariable("RABBITMQ_REPORT_EXCHANGE"), string.Empty, props, MessageBytes);

            model.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));

            return Ok();
        }

        private static void ConfigSchema(IModel model, string exchangeName, string queueName)
        {
            //Unrouted
            model.ExchangeDeclare($"{exchangeName}_unrouted", "fanout", true, false);
            model.QueueDeclare($"{queueName}_unrouted", true, false, false);
            model.QueueBind($"{queueName}_unrouted", $"{exchangeName}_unrouted", string.Empty);

            //Deadletter
            model.ExchangeDeclare($"{exchangeName}_deadletter", "fanout", true, false);
            model.QueueDeclare($"{queueName}_deadletter", true, false, false);
            model.QueueBind($"{queueName}_deadletter", $"{exchangeName}_deadletter", string.Empty);

            model.ExchangeDeclare(exchangeName, "direct", true, false, new Dictionary<string, object>() {
                { "alternate-exchange", $"{exchangeName}_unrouted" }
            });
            model.QueueDeclare(queueName, true, false, false, new Dictionary<string, object>() {
                { "x-dead-letter-exchange", $"{exchangeName}_deadletter" },
                { "alternate-exchange", $"{exchangeName}_unrouted" }
            });
            model.QueueBind(queueName, exchangeName, string.Empty);
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
