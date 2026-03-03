using System.Text;
using System.Text.Json;
using HUBDTE.Application.Interfaces.Persistence;
using HUBDTE.Domain.Entities;
using HUBDTE.Infrastructure.Messaging;
using HUBDTE.Infrastructure.Messaging.Options;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HUBDTE.WorkerHost.Consumers;

public class RabbitDlqReprocessorWorker : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly RabbitChannelFactory _rabbitFactory;
    private readonly RabbitMqOptions _rabbit;
    private readonly IServiceProvider _sp;
    private readonly ILogger<RabbitDlqReprocessorWorker> _logger;
    private readonly string _dlqQueueName;
    private readonly string _mainQueueName;

    private IConnection? _connection;
    private IModel? _channel;
    private string? _consumerTag;
    private readonly object _sync = new();

    private CancellationToken _stoppingToken;

    public RabbitDlqReprocessorWorker(
        RabbitChannelFactory rabbitFactory,
        IOptions<RabbitMqOptions> rabbit,
        IServiceProvider sp,
        ILogger<RabbitDlqReprocessorWorker> logger,
        string dlqQueueName,
        string mainQueueName)
    {
        _rabbitFactory = rabbitFactory;
        _rabbit = rabbit.Value;
        _sp = sp;
        _logger = logger;
        _dlqQueueName = dlqQueueName;
        _mainQueueName = mainQueueName;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "✅ RabbitDlqReprocessorWorker iniciado. DLQ={Dlq} -> Main={Main} Exchange={Exchange}",
            _dlqQueueName, _mainQueueName, _rabbit.Exchange);

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

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
                }

                _logger.LogWarning("⚠️ Conexión/canal a Rabbit cerrados. Reintentando...");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "⚠️ Rabbit no disponible o falló el consumo DLQ. Reintentando en {Delay}s...",
                    ReconnectDelay.TotalSeconds);
            }

            Cleanup();

            if (!stoppingToken.IsCancellationRequested)
                await Task.Delay(ReconnectDelay, stoppingToken);
        }
    }

    private void EnsureConnected() 
    {
        lock (_sync)
        {
            if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
                return;

            Cleanup_NoLock();

            var (conn, ch) = _rabbitFactory.Create($"HUBDTE.WorkerHost.DlqReprocessor:{_dlqQueueName}");
            _connection = conn;
            _channel = ch;
            _channel.BasicQos(0, 1, false);

            _logger.LogInformation("✅ Conectado a Rabbit (DLQ reprocessor). DLQ={Dlq}", _dlqQueueName);
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
            consumer.Received += OnReceivedAsync;

            _consumerTag = _channel.BasicConsume(queue: _dlqQueueName, autoAck: false, consumer: consumer);

            _logger.LogInformation("✅ Consumiendo DLQ: {DlqQueue} (consumerTag={ConsumerTag})", _dlqQueueName, _consumerTag);
        }
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var msgJson = Encoding.UTF8.GetString(ea.Body.ToArray());

        try
        {
            using var doc = JsonDocument.Parse(msgJson);
            var root = doc.RootElement;

            var filialCode = root.GetProperty("filialCode").GetString()!;
            var docEntry = root.GetProperty("docEntry").GetInt64();
            var tipoDte = root.GetProperty("tipoDte").GetInt32();

            using var logScope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["filialCode"] = filialCode,
                ["docEntry"] = docEntry,
                ["tipoDte"] = tipoDte,
                ["dlq"] = _dlqQueueName,
                ["mainQueue"] = _mainQueueName,
                ["correlationId"] = ea.BasicProperties?.CorrelationId ?? ""
            });

            try
            {
                await using var scope = _sp.CreateAsyncScope();

                var sapRepo = scope.ServiceProvider.GetRequiredService<ISapDocumentRepository>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                var sapDoc = await sapRepo.FindByKeyAsync(filialCode, docEntry, tipoDte, _stoppingToken);

                if (sapDoc != null)
                {
                    sapDoc.Status = SapDocumentStatus.Pending;

                    var stamp = $"{DateTime.UtcNow:O} | Reprocesado manualmente desde DLQ ({_dlqQueueName})";
                    sapDoc.ErrorReason = string.IsNullOrWhiteSpace(sapDoc.ErrorReason)
                        ? stamp
                        : $"{sapDoc.ErrorReason} || {stamp}";

                    sapDoc.AttemptCount = 0;

                    await uow.SaveChangesAsync(_stoppingToken);

                    _logger.LogInformation("📝 SQL actualizado: Status=Pending, AttemptCount=0, ErrorReason+=Reprocesado desde DLQ");
                }
                else
                {
                    _logger.LogWarning("⚠️ SapDocument no encontrado. Se reencola igual.");
                }
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "❌ Error actualizando SQL en reproceso DLQ");
            }

            var props = _channel!.CreateBasicProperties();
            props.Persistent = true;

            if (!string.IsNullOrWhiteSpace(ea.BasicProperties?.CorrelationId))
                props.CorrelationId = ea.BasicProperties.CorrelationId;

            props.Headers = ea.BasicProperties?.Headers != null
                ? new Dictionary<string, object>(ea.BasicProperties.Headers)
                : new Dictionary<string, object>();

            RabbitHeaders.SetAttempt(props, 0);

            _channel.BasicPublish(
                exchange: _rabbit.Exchange,
                routingKey: _mainQueueName,
                basicProperties: props,
                body: ea.Body);

            _logger.LogWarning("♻️ Reencolado desde DLQ -> Main. DLQ={Dlq} Main={Main}", _dlqQueueName, _mainQueueName);

            _channel.BasicAck(ea.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Mensaje inválido en DLQ {DlqQueue}: {Body}", _dlqQueueName, msgJson);
            _channel!.BasicAck(ea.DeliveryTag, false);
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