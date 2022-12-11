using Minio;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using ReportWorker;
using System.Net;
using System.Reflection;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton((sp) => new ConnectionFactory()
        {
            Uri = new Uri(Environment.GetEnvironmentVariable("RABBITMQ_CONNECTIONSTRING") ?? ""),
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
            AutomaticRecoveryEnabled = true,
            DispatchConsumersAsync = true,
        });

        services.AddSingleton(sp => Policy
                .Handle<BrokerUnreachableException>()
                .WaitAndRetry(int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_CONNECTMAXATTEMPTS") ?? "2"), retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)))
                .Execute(() => sp.GetRequiredService<ConnectionFactory>().CreateConnection()));

        services.AddTransient((sp) => sp.GetRequiredService<IConnection>().CreateModel());

        services.AddSingleton((sp) => new MinioClient()
                .WithEndpoint(Environment.GetEnvironmentVariable("MINIO_ENDPOINT"))
                .WithProxy(new WebProxy(Environment.GetEnvironmentVariable("MINIO_ENDPOINT_PROXY")))
                .WithCredentials(Environment.GetEnvironmentVariable("MINIO_ACCESSKEY"), Environment.GetEnvironmentVariable("MINIO_SECRETKEY"))
                .WithHttpClient(new HttpClient(new HttpClientHandler() { Proxy = new WebProxy(Environment.GetEnvironmentVariable("MINIO_ENDPOINT_PROXY")) }))
                .Build());

        services.AddOpenTelemetryTracing(openTelemetryBuilder =>
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
                .AddSource(nameof(Worker))
                .SetResourceBuilder(GetResourceBuilder())
                .AddJaegerExporter(opts =>
                {
                    opts.AgentHost = "jaeger";
                    opts.AgentPort = 6831;
                    opts.ExportProcessorType = ExportProcessorType.Simple;
                });
        });

        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();

static ResourceBuilder GetResourceBuilder()
{    
    var version = Assembly
        .GetExecutingAssembly()
        .GetCustomAttribute<AssemblyFileVersionAttribute>()
        .Version;

    return ResourceBuilder
        .CreateEmpty()
        .AddService("ReportWorker", serviceVersion: version)
        .AddAttributes(
            new KeyValuePair<string, object>[]
            {                        
                new("host.name", Environment.MachineName),
            })
        .AddEnvironmentVariableDetector();
}