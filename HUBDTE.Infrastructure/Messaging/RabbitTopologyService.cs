using HUBDTE.Infrastructure.Messaging.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace HUBDTE.Infrastructure.Messaging;

public sealed class RabbitTopologyService : IRabbitTopologyService
{
    private const string DlqExchangeName = "documents.dlq.exchange";

    private readonly RabbitChannelFactory _factory;
    private readonly RabbitMqOptions _rabbit;
    private readonly ILogger<RabbitTopologyService> _logger;

    public RabbitTopologyService(
        RabbitChannelFactory factory,
        IOptions<RabbitMqOptions> rabbit,
        ILogger<RabbitTopologyService> logger)
    {
        _factory = factory;
        _rabbit = rabbit.Value;
        _logger = logger;
    }

    public void EnsureBaseExchanges()
    {
        var (connection, channel) = Create("HUBDTE.Topology.Base");
        using var conn = connection;
        using var ch = channel;

        EnsureBaseExchanges(ch);
    }

    public void EnsureMainQueueWithDlq(string mainQueue)
    {
        var (connection, channel) = Create($"HUBDTE.Topology.Main:{mainQueue}");
        using var conn = connection;
        using var ch = channel;

        EnsureBaseExchanges(ch);

        var dlqQueue = QueueNames.Dlq(mainQueue);

        ch.QueueDeclare(
            queue: dlqQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        ch.QueueBind(queue: dlqQueue, exchange: DlqExchangeName, routingKey: dlqQueue);
        ch.QueueBind(queue: dlqQueue, exchange: _rabbit.Exchange, routingKey: dlqQueue);

        var mainArgs = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = DlqExchangeName,
            ["x-dead-letter-routing-key"] = dlqQueue
        };

        ch.QueueDeclare(
            queue: mainQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: mainArgs);

        ch.QueueBind(queue: mainQueue, exchange: _rabbit.Exchange, routingKey: mainQueue);

        _logger.LogInformation("✅ Topology main/dlq OK. Main={Main} Dlq={Dlq}", mainQueue, dlqQueue);
    }

    public void EnsureRetryQueues(string mainQueue, int[] delaysSeconds)
    {
        var (connection, channel) = Create($"HUBDTE.Topology.Retry:{mainQueue}");
        using var conn = connection;
        using var ch = channel;

        EnsureBaseExchanges(ch);

        for (var i = 0; i < delaysSeconds.Length; i++)
        {
            var attempt = i + 1;
            var retryQueue = QueueNames.Retry(mainQueue, attempt);
            var ttlMs = checked(delaysSeconds[i] * 1000);

            var retryArgs = new Dictionary<string, object>
            {
                ["x-message-ttl"] = ttlMs,
                ["x-dead-letter-exchange"] = _rabbit.Exchange,
                ["x-dead-letter-routing-key"] = mainQueue
            };

            ch.QueueDeclare(
                queue: retryQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: retryArgs);

            ch.QueueBind(queue: retryQueue, exchange: _rabbit.Exchange, routingKey: retryQueue);
        }
    }

    private void EnsureBaseExchanges(IModel ch)
    {
        ch.ExchangeDeclare(
            exchange: _rabbit.Exchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);

        ch.ExchangeDeclare(
            exchange: DlqExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);
    }

    private (IConnection connection, IModel channel) Create(string name)
        => _factory.Create(name);
}