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

            model.ExchangeDeclare(Environment.GetEnvironmentVariable("RABBITMQ_REPORT_EXCHANGE"), "direct", true, false);
            model.QueueDeclare(Environment.GetEnvironmentVariable("RABBITMQ_REPORT_QUEUE"), true, false, false);
            model.QueueBind(Environment.GetEnvironmentVariable("RABBITMQ_REPORT_QUEUE"), Environment.GetEnvironmentVariable("RABBITMQ_REPORT_EXCHANGE"), string.Empty);

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
    }
}
