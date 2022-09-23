using NotificationHub.Workers;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

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

builder.Services.AddHostedService<NotificationWorker>();

var app = builder.Build();

app.UseCors("AllowOrigin");
app.MapHub<NotificationHub.Hubs.NotificationHub>("/notification");

await app.RunAsync();