using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using Prometheus;
using Prometheus.DotNetRuntime;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using ReportApi.Controllers;
using System.Diagnostics;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(c =>
{
    c.AddPolicy("AllowOrigin", options => options.AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddControllers();

builder.Services.AddSingleton((sp) => new ConnectionFactory()
{
    Uri = new Uri(Environment.GetEnvironmentVariable("RABBITMQ_CONNECTIONSTRING") ?? ""),
    NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
    AutomaticRecoveryEnabled = true
});

builder.Services.AddSingleton(sp => Policy
        .Handle<BrokerUnreachableException>()
        .WaitAndRetry(int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_CONNECTMAXATTEMPTS") ?? "2"), retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)))
        .Execute(() => sp.GetRequiredService<ConnectionFactory>().CreateConnection()));

builder.Services.AddTransient((sp) => sp.GetRequiredService<IConnection>().CreateModel());

builder.Services.AddOpenTelemetryTracing(openTelemetryBuilder =>
{
    openTelemetryBuilder
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
            options.EnrichWithHttpRequest = ((activity, request) => Enrich(activity, request));
        })
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
        .AddSource(nameof(ReportController))
        .SetResourceBuilder(GetResourceBuilder(builder.Environment))
        .AddJaegerExporter(opts =>
        {
            opts.AgentHost = "jaeger";
            opts.AgentPort = 6831;
            opts.ExportProcessorType = ExportProcessorType.Simple;
        });
});

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

app.MapControllers();

app.Run();

static void Enrich(Activity activity, object obj)
{
    if (obj is HttpRequest request)
    {
        var context = request.HttpContext;
        activity.AddTag("http.client_ip", context.Connection.RemoteIpAddress);
        activity.AddTag("http.request_content_length", request.ContentLength);
        activity.AddTag("http.request_content_type", request.ContentType);
    }
    else if (obj is HttpResponse response)
    {
        activity.AddTag("http.response_content_length", response.ContentLength);
        activity.AddTag("http.response_content_type", response.ContentType);
    }
}

static ResourceBuilder GetResourceBuilder(IWebHostEnvironment webHostEnvironment)
{
    var version = Assembly
        .GetExecutingAssembly()
        .GetCustomAttribute<AssemblyFileVersionAttribute>()
        .Version;

    return ResourceBuilder
        .CreateEmpty()
        .AddService(webHostEnvironment.ApplicationName, serviceVersion: version)
        .AddAttributes(
            new KeyValuePair<string, object>[]
            {
                new("deployment.environment", webHostEnvironment.EnvironmentName),
                new("host.name", Environment.MachineName),
            })
        .AddEnvironmentVariableDetector();
}