using HUBDTE.Application.DocumentProcessing;
using HUBDTE.Infrastructure.Messaging;
using HUBDTE.Infrastructure.Messaging.Options;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace HUBDTE.WorkerHost.Consumers;

public class RabbitConsumerWorker : BackgroundService
{
    private const string DlqExchangeName = "documents.dlq.exchange";
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MetricsEvery = TimeSpan.FromSeconds(30);

    private readonly IServiceProvider _sp;
    private readonly ILogger<RabbitConsumerWorker> _logger;
    private readonly IConfiguration _config;
    private readonly RabbitChannelFactory _rabbitFactory;
    private readonly IRetryPolicyService _retryPolicy;
    private readonly RabbitMqOptions _rabbit;
    private readonly string _queueName;

    private IConnection? _connection;
    private IModel? _channel;

    private readonly string _dlqQueueName;

    private string? _consumerTag;
    private readonly object _sync = new();
    private CancellationToken _stoppingToken;

    private long _ok;
    private long _failed;
    private long _toRetry;
    private long _toDlq;
    private long _invalid;
    private long _requeued;

    private DateTime _lastMetrics = DateTime.UtcNow;

    public RabbitConsumerWorker(
    IServiceProvider sp,
    ILogger<RabbitConsumerWorker> logger,
    IConfiguration config,
    RabbitChannelFactory rabbitFactory,
    IRetryPolicyService retryPolicy,
    IOptions<RabbitMqOptions> rabbit,
    string queueName)
    { 
        _sp = sp;
        _logger = logger;
        _config = config;
        _rabbitFactory = rabbitFactory;
        _retryPolicy = retryPolicy;
        _queueName = queueName;
        _dlqQueueName = QueueNames.Dlq(queueName);

        _rabbit = rabbit.Value;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "✅ RabbitConsumerWorker iniciado. Queue={Queue} Exchange={Exchange} Endpoint={Endpoint}",
            _queueName,
            _rabbit.Exchange,
            _rabbitFactory.GetEndpointInfo());

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        _lastMetrics = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                EnsureConnected();
                EnsureConsuming();

                while (!stoppingToken.IsCancellationRequested &&
                       _connection is { IsOpen: true } &&
                       _channel is { IsOpen: true })
                {
                    await Task.Delay(1000, stoppingToken);
                    EmitMetricsIfDue();
                }

                _logger.LogWarning("⚠️ Conexión/canal a Rabbit cerrados. Reintentando...");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Rabbit no disponible o falló el consumo. Reintentando en {Delay}s...",
                    ReconnectDelay.TotalSeconds);
            }

            Cleanup();

            if (!stoppingToken.IsCancellationRequested)
                await Task.Delay(ReconnectDelay, stoppingToken);
        }
    }

    private void EmitMetricsIfDue()
    {
        var now = DateTime.UtcNow;
        if (now - _lastMetrics < MetricsEvery) return;

        _lastMetrics = now;

        _logger.LogInformation(
            "📊 Metrics Queue={Queue} ok={Ok} failed={Failed} retry={Retry} dlq={Dlq} invalid={Invalid} requeued={Requeued}",
            _queueName,
            Interlocked.Read(ref _ok),
            Interlocked.Read(ref _failed),
            Interlocked.Read(ref _toRetry),
            Interlocked.Read(ref _toDlq),
            Interlocked.Read(ref _invalid),
            Interlocked.Read(ref _requeued));
    }

    private void EnsureConnected() 
    {
        lock (_sync)
        {
            if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
                return;

            Cleanup_NoLock();

            var (conn, ch) = _rabbitFactory.Create($"HUBDTE.WorkerHost.Consumer:{_queueName}");
            _connection = conn;
            _channel = ch;
            _channel.BasicQos(0, 1, false);

            _logger.LogInformation("✅ Conectado a Rabbit (consumer). Queue={Queue}", _queueName);
        }
    }

    private void EnsureConsuming()
    {
        lock (_sync)
        {
            if (_channel is not { IsOpen: true })
                throw new InvalidOperationException("Rabbit channel no está disponible.");

            if (!string.IsNullOrWhiteSpace(_consumerTag))
                return;

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += OnMessageReceivedAsync;

            _consumerTag = _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);

            _logger.LogInformation("✅ Consumiendo cola: {Queue} (consumerTag={ConsumerTag})", _queueName, _consumerTag);
        }
    }

    //Punto Inicial
    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        if (_stoppingToken.IsCancellationRequested)
        {
            TryNack(ea.DeliveryTag, requeue: true);
            return;
        }

        var msgJson = Encoding.UTF8.GetString(ea.Body.ToArray());

        string filialCode;
        long docEntry;
        int tipoDte;

        var attemptFromHeader = RabbitHeaders.GetAttempt(ea.BasicProperties);
        var attempt = attemptFromHeader + 1;

        var correlationId = ea.BasicProperties?.CorrelationId;
        var msgType = GetHeaderString(ea.BasicProperties, RabbitHeaders.MessageType);

        try
        {
            using var doc = JsonDocument.Parse(msgJson);
            var root = doc.RootElement;

            filialCode = root.GetProperty("filialCode").GetString()!;
            docEntry = root.GetProperty("docEntry").GetInt64();
            tipoDte = root.GetProperty("tipoDte").GetInt32();

            if (string.IsNullOrWhiteSpace(filialCode))
                throw new Exception("filialCode vacío");
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _invalid);

            _logger.LogError(ex, "❌ Mensaje inválido en cola {Queue}. Se enviará a DLQ. Body={Body}", _queueName, msgJson);

            try
            {
                PublishToDlq(ea, attempt, invalid: true, error: ex.Message);
                AckOrThrow(ea.DeliveryTag);
                Interlocked.Increment(ref _toDlq);
            }
            catch (Exception pubEx)
            {
                _logger.LogError(pubEx, "❌ No se pudo publicar inválido a DLQ. Requeue.");
                Interlocked.Increment(ref _requeued);
                NackOrThrow(ea.DeliveryTag, requeue: true);
            }

            return;
        }

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["filialCode"] = filialCode,
            ["docEntry"] = docEntry,
            ["tipoDte"] = tipoDte,
            ["queue"] = _queueName,
            ["attempt"] = attempt,
            ["correlationId"] = correlationId ?? "",
            ["messageType"] = msgType ?? ""
        });

        try
        {

            await using var scope = _sp.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<IDocumentProcessor>();

            var sim = _config.GetSection("FailureSimulation");
            var simEnabled = sim.GetValue<bool>("Enabled");
            var simTipo = sim.GetValue<int>("FailTipoDte");
            var simFailAlways = sim.GetValue<bool>("FailAlways");

            if (simEnabled && tipoDte == simTipo)
            {
                if (simFailAlways || attemptFromHeader == 0)
                    throw new Exception($"Falla controlada para pruebas. tipoDte={tipoDte}, attempt={attemptFromHeader}");
            }

            await processor.ProcessAsync(filialCode, docEntry, tipoDte, attempt, _stoppingToken);

            Interlocked.Increment(ref _ok);
            AckOrThrow(ea.DeliveryTag);
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref _requeued);
            NackOrThrow(ea.DeliveryTag, requeue: true);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failed);
            _logger.LogError(ex, "❌ Error procesando mensaje");           

            try
            {
                PublishRetryOrDlq(ea, attempt);
                AckOrThrow(ea.DeliveryTag);

                if (_retryPolicy.ShouldRetry(attempt)) Interlocked.Increment(ref _toRetry);
                else Interlocked.Increment(ref _toDlq);
            }
            catch (Exception pubEx)
            {
                _logger.LogError(pubEx, "❌ Falló publish a retry/dlq. Requeue para no perder el mensaje.");
                Interlocked.Increment(ref _requeued);
                NackOrThrow(ea.DeliveryTag, requeue: true);
            }
        }
    }

    private void PublishRetryOrDlq(BasicDeliverEventArgs ea, int attempt)
    {
        var props = CreateAttemptProperties(attempt, ea);
        var retryQueue = _retryPolicy.GetRetryQueueForAttempt(_queueName, attempt);

        if (!string.IsNullOrWhiteSpace(retryQueue) && _retryPolicy.ShouldRetry(attempt))
        {
            BasicPublishSafe(_rabbit.Exchange, retryQueue, props, ea.Body);
            _logger.LogWarning("↩️ Enviado a retry {RetryQueue}. Attempt={Attempt}", retryQueue, attempt);
            return;
        }

        BasicPublishSafe(DlqExchangeName, _dlqQueueName, props, ea.Body);
        _logger.LogError("🧨 Enviado a DLQ {DlqQueue}. Attempt={Attempt}", _dlqQueueName, attempt);
    }

    private void PublishToDlq(BasicDeliverEventArgs ea, int attempt, bool invalid, string? error)
    {
        var props = CreateAttemptProperties(attempt, ea);
        RabbitHeaders.SetInvalid(props, invalid);
        RabbitHeaders.SetError(props, error);

        BasicPublishSafe(DlqExchangeName, _dlqQueueName, props, ea.Body);
    }

    private IBasicProperties CreateAttemptProperties(int attempt, BasicDeliverEventArgs ea)
    {
        var ch = GetChannelOrThrow();
        var props = ch.CreateBasicProperties();
        props.Persistent = true;

        var incomingCorrelationId = ea.BasicProperties?.CorrelationId;
        if (!string.IsNullOrWhiteSpace(incomingCorrelationId))
            props.CorrelationId = incomingCorrelationId;

        RabbitHeaders.EnsureCorrelationId(props, $"{_queueName}:{attempt}:{Guid.NewGuid():N}");

        props.Headers = ea.BasicProperties?.Headers != null
            ? new Dictionary<string, object>(ea.BasicProperties.Headers)
            : new Dictionary<string, object>();

        RabbitHeaders.SetAttempt(props, attempt);

        return props;
    }

    private void BasicPublishSafe(string exchange, string routingKey, IBasicProperties props, ReadOnlyMemory<byte> body)
    {
        var ch = GetChannelOrThrow();
        ch.BasicPublish(exchange: exchange, routingKey: routingKey, basicProperties: props, body: body);
    }

    private IModel GetChannelOrThrow()
    {
        lock (_sync)
        {
            if (_channel is null || !_channel.IsOpen)
                throw new InvalidOperationException("Rabbit channel no está disponible/abierto.");
            return _channel;
        }
    }

    private void AckOrThrow(ulong deliveryTag)
    {
        var ch = GetChannelOrThrow();
        ch.BasicAck(deliveryTag, multiple: false);
    }

    private void NackOrThrow(ulong deliveryTag, bool requeue)
    {
        var ch = GetChannelOrThrow();
        ch.BasicNack(deliveryTag, multiple: false, requeue: requeue);
    }

    private void TryNack(ulong deliveryTag, bool requeue)
    {
        try { NackOrThrow(deliveryTag, requeue); }
        catch (Exception ex) { _logger.LogWarning(ex, "⚠️ Falló BasicNack (probablemente canal cerrado)."); }
    }

    private static string? GetHeaderString(IBasicProperties? props, string key)
    {
        try
        {
            if (props?.Headers == null) return null;
            if (!props.Headers.TryGetValue(key, out var raw) || raw is null) return null;

            if (raw is byte[] bytes) return Encoding.UTF8.GetString(bytes);
            return raw.ToString();
        }
        catch
        {
            return null;
        }
    }

    private void Cleanup()
    {
        lock (_sync) { Cleanup_NoLock(); }
    }

    private void Cleanup_NoLock()
    {
        _consumerTag = null;

        try { _channel?.Close(); } catch { }
        try { _connection?.Close(); } catch { }

        try { _channel?.Dispose(); } catch { }
        try { _connection?.Dispose(); } catch { }

        _channel = null;
        _connection = null;
    }

    public override void Dispose()
    {
        Cleanup();
        base.Dispose();
    }
}