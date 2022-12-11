using NotificationHub.Workers;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton((sp) => new ConnectionFactory()
{
    Uri = new Uri(Environment.GetEnvironmentVariable("RABBITMQ_CONNECTIONSTRING")),
    NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
    AutomaticRecoveryEnabled = true,
    DispatchConsumersAsync = true,
});

builder.Services.AddSingleton(sp => Policy
    .Handle<BrokerUnreachableException>()
    .WaitAndRetry(int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_CONNECTMAXATTEMPTS")), retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)))
    .Execute(() => sp.GetRequiredService<ConnectionFactory>().CreateConnection()));

builder.Services.AddTransient((sp) => sp.GetRequiredService<IConnection>().CreateModel());
builder.Services.AddCors(c =>
{
    c.AddPolicy("AllowOrigin", options => options.AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddOpenTelemetryTracing(openTelemetryBuilder =>
{
    openTelemetryBuilder        
        .AddHttpClientInstrumentation((options) =>
        {
            options.RecordException = true;
            options.FilterHttpRequestMessage = (httpRequestMessage) =>
            {
                return httpRequestMessage.Method.Equals(HttpMethod.Get) ||
                       httpRequestMessage.Method.Equals(HttpMethod.Post) ||
                       httpRequestMessage.Method.Equals(HttpMethod.Put) ||
                       httpRequestMessage.Method.Equals(HttpMethod.Delete);
            };
            options.EnrichWithException = (activity, exception) =>
            {
                activity.SetTag("stacktrace", exception.StackTrace);
            };
        })
        .AddSource(nameof(NotificationWorker))
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("NotificationHub"))
        .AddJaegerExporter(opts =>
        {
            opts.AgentHost = "jaeger";
            opts.AgentPort = 6831;
            opts.ExportProcessorType = ExportProcessorType.Simple;
        });
});

builder.Services.AddHostedService<NotificationWorker>();

var app = builder.Build();

app.UseCors("AllowOrigin");
app.MapHub<NotificationHub.Hubs.NotificationHub>("/notification");

await app.RunAsync();