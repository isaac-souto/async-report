# Relatório

Repositório de exemplo de estudo com fluxo de geração de relatório de forma assíncrona utilizando RabbitMQ, armazenamento dos arquivo no Minio e envio do link de download através de notificação no front end com SignalR além do armazenamento de tracking com OpenTelemetry e Jaeger.

<p>
  <img src=".github/report.png" width="800" alt="Report" />  
</p>

## Comandos

- docker compose up

## Portas

- Front End (ReactJS) - [http://localhost:3000](http://localhost:3000)
- Back End (Api Asp.Net Core/Worker) - [http://localhost:81](http://localhost:81)
- Hub SignalR (Asp.Net Core) - [http://localhost:82](http://localhost:82)
- RabbitMQ Management - [http://localhost:15672](http://localhost:15672)
- RabbitMQ - [http://localhost:5672](http://localhost:5672)
- Minio Console - [http://localhost:9001](http://localhost:9001)
- Minio Api - [http://localhost:9000](http://localhost:9000)
- Jaeger (Traces) - [http://localhost:16686](http://localhost:16686)
- Phometeus (Métricas) - [http://localhost:9090](http://localhost:9090)
- Grafana (Dashboard) - [http://localhost:3001](http://localhost:3001)
- Grafana Loki (Logs)
