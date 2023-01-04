using NotificationHub.Workers;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using Prometheus;
using Prometheus.DotNetRuntime;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);

builder.Host.ConfigureLogging((_, loggingBuilder) => loggingBuilder.ClearProviders())
                .UseSerilog((ctx, cfg) =>
                {
                    cfg.Enrich.WithProperty("Application", ctx.HostingEnvironment.ApplicationName)
                       .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
                       .WriteTo.Console(new RenderedCompactJsonFormatter())
                       .WriteTo.GrafanaLoki(uri: $"{Environment.GetEnvironmentVariable("LOKI__ENDPOINT")}:{Environment.GetEnvironmentVariable("LOKI__PORT")}");
                });

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

var builderCollector = DotNetRuntimeStatsBuilder.Default();

builderCollector = DotNetRuntimeStatsBuilder.Customize()
    .WithContentionStats(CaptureLevel.Informational)
    .WithGcStats(CaptureLevel.Verbose)
    .WithThreadPoolStats(CaptureLevel.Informational)
    .WithExceptionStats(CaptureLevel.Errors)
    .WithJitStats();

builderCollector.RecycleCollectorsEvery(new TimeSpan(0, 20, 0));

builderCollector.StartCollecting();

var app = builder.Build();

app.UseCors("AllowOrigin");
app.UseHttpMetrics();
app.UseMetricServer();
app.UseSerilogRequestLogging();

app.MapHub<NotificationHub.Hubs.NotificationHub>("/notification");

await app.RunAsync();