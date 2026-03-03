using HUBDTE.Domain.Entities;

namespace HUBDTE.Application.Interfaces.Persistence;

public interface IOutboxMessageRepository
{
    Task<bool> HasPendingForSapDocumentAsync(Guid sapDocumentId, CancellationToken ct);

    Task<int> RescueStuckProcessingAsync(DateTime utcNow, TimeSpan stuckAfter, string reason, CancellationToken ct);

    Task<int> RescueStaleLocksAsync(DateTime utcNow, TimeSpan staleAfter, string reason, CancellationToken ct);

    Task<int> ClaimBatchAsync(Guid lockId, DateTime utcNow, int batchSize, int maxAttempts, CancellationToken ct);

    Task<List<OutboxMessage>> GetClaimedBatchAsync(Guid lockId, int batchSize, CancellationToken ct);

    void Add(OutboxMessage msg);
}
