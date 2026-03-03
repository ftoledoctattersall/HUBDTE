using HUBDTE.Application.Interfaces.Messaging;
using HUBDTE.Application.Interfaces.Persistence;
using HUBDTE.Infrastructure.Messaging;

namespace HUBDTE.WorkerHost.HostedServices
{
    public class OutboxPublisherHostedService : BackgroundService
    {
        private const int BatchSize = 50;
        private const int MaxPublishAttempts = 10;

        private static readonly TimeSpan ProcessingStuckAfter = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan LockStaleAfter = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OutboxPublisherHostedService> _logger;

        public OutboxPublisherHostedService(IServiceProvider serviceProvider, ILogger<OutboxPublisherHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("✅ OutboxPublisherHostedService iniciado");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var didWork = await TickAsync(stoppingToken);
                    if (!didWork)
                        await Task.Delay(IdleDelay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error general en OutboxPublisherHostedService");
                    await Task.Delay(IdleDelay, stoppingToken);
                }
            }
        }

        private async Task<bool> TickAsync(CancellationToken ct)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();

            var repo = scope.ServiceProvider.GetRequiredService<IOutboxMessageRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var publisher = scope.ServiceProvider.GetRequiredService<RabbitMqPublisher>();
            var routing = scope.ServiceProvider.GetRequiredService<IMessageRoutingResolver>();

            try
            {
                publisher.WarmUp();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ RabbitMQ no disponible. Se omite el ciclo (no se toman mensajes).");
                return false;
            }

            var now = DateTime.UtcNow;

            var rescuedProcessing = await repo.RescueStuckProcessingAsync(
                utcNow: now,
                stuckAfter: ProcessingStuckAfter,
                reason: "Rescatado automáticamente: estaba en Processing demasiado tiempo (posible caída del servicio).",
                ct: ct);

            if (rescuedProcessing > 0)
                _logger.LogWarning("🛟 Rescatados {Count} stuck Processing -> Pending", rescuedProcessing);

            var rescuedLocks = await repo.RescueStaleLocksAsync(
                utcNow: now,
                staleAfter: LockStaleAfter,
                reason: "Rescatado automáticamente: lock viejo liberado (posible caída del worker).",
                ct: ct);

            if (rescuedLocks > 0)
                _logger.LogWarning("🔓 Liberados {Count} locks viejos (Pending con LockId)", rescuedLocks);

            var lockId = Guid.NewGuid();
            var claimed = await repo.ClaimBatchAsync(lockId, now, BatchSize, MaxPublishAttempts, ct);

            if (claimed == 0)
                return false;

            var batch = await repo.GetClaimedBatchAsync(lockId, BatchSize, ct);
            if (batch.Count == 0)
                return false;

            foreach (var msg in batch)
            {
                using var logScope = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["outboxId"] = msg.Id,
                    ["sapDocumentId"] = msg.SapDocumentId,
                    ["publishAttempts"] = msg.PublishAttempts,
                    ["status"] = msg.Status.ToString(),
                    ["lockId"] = msg.LockId?.ToString()
                });

                try
                {
                    var routingKey = routing.ResolveRoutingKey(msg.MessageType);
                    var correlationId = $"outbox:{msg.Id:N}";

                    msg.CorrelationId = correlationId;
                    msg.MessageTypeHeader = msg.MessageType;
                    await uow.SaveChangesAsync(ct);

                    publisher.PublishConfirmed(
                        routingKey: routingKey,
                        bodyJson: msg.Body,
                        correlationId: correlationId,
                        messageType: msg.MessageType);

                    msg.MarkPublished();
                    await uow.SaveChangesAsync(ct);

                    _logger.LogInformation(
                        "✅ Outbox publicado a routingKey={RoutingKey} (MessageType={MessageType})",
                        routingKey,
                        msg.MessageType);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    msg.RescueStuckProcessing("Cancelación solicitada: vuelve a Pending.");
                    await uow.SaveChangesAsync(ct);
                    throw;
                }
                catch (Exception ex)
                {
                    if (msg.PublishAttempts + 1 >= MaxPublishAttempts)
                    {
                        msg.MarkFailed(ex);
                        _logger.LogError(ex, "🧨 Outbox quedó Failed (max attempts alcanzado)");
                    }
                    else
                    {
                        msg.MarkRetryableFailure(ex);
                        _logger.LogWarning(ex, "↩️ Falló publicación, se reintentará. Attempts={Attempts}", msg.PublishAttempts);
                    }

                    await uow.SaveChangesAsync(ct);
                }
            }

            return true;
        }
    }
}