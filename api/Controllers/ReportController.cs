using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using ReportApi.Models;
using System.Text;
using System.Text.Json;

namespace ReportApi.Controllers
{
    public class ReportController : ControllerBase
    {
        [HttpPost]
        [Route("api/report/{userId:guid}")]
        public IActionResult Post([FromServices] IModel model, Guid userId)
        {
            model.ConfirmSelect();

            ConfigSchema(model, Environment.GetEnvironmentVariable("RABBITMQ_REPORT_EXCHANGE"), Environment.GetEnvironmentVariable("RABBITMQ_REPORT_QUEUE"));

            var prop = model.CreateBasicProperties();
            prop.DeliveryMode = 2;
            prop.ContentType = "application/json";

            var MessageBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new ReportModel
            {
                UserId = userId
            }));

            model.BasicPublish(Environment.GetEnvironmentVariable("RABBITMQ_REPORT_EXCHANGE"), string.Empty, prop, MessageBytes);

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
    }
}
