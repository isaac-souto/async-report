using Minio;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using ReportWorker;
using System.Net;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<Worker>();

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
    })
    .Build();

await host.RunAsync();
