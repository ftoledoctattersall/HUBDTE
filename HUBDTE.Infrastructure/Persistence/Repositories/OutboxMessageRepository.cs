using HUBDTE.Application.Interfaces.Persistence;
using HUBDTE.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace HUBDTE.Infrastructure.Persistence.Repositories
{
    public class OutboxMessageRepository : IOutboxMessageRepository
    {
        private readonly AppDbContext _db;
        public OutboxMessageRepository(AppDbContext db) => _db = db;

        public Task<bool> HasPendingForSapDocumentAsync(Guid sapDocumentId, CancellationToken ct)
        {
            return _db.OutboxMessages.AnyAsync(x =>
                x.SapDocumentId == sapDocumentId && x.Status == OutboxStatus.Pending, ct);
        }

        public void Add(OutboxMessage msg) => _db.OutboxMessages.Add(msg);

        public async Task<int> RescueStuckProcessingAsync(DateTime utcNow, TimeSpan stuckAfter, string reason, CancellationToken ct)
        {
            var stuckBefore = utcNow.Subtract(stuckAfter);

            return await _db.Database.ExecuteSqlRawAsync(@"
UPDATE dbo.OutboxMessages
SET
    Status = {0},
    ProcessingStartedAt = NULL,
    LockId = NULL,
    LockedAt = NULL,
    Error = {1}
WHERE
    Status = {2}
    AND PublishedAt IS NULL
    AND ProcessingStartedAt IS NOT NULL
    AND ProcessingStartedAt < {3};
",
            new object[]
            {
                (byte)OutboxStatus.Pending,
                reason,
                (byte)OutboxStatus.Processing,
                stuckBefore
            }, ct);
        }

        public async Task<int> RescueStaleLocksAsync(DateTime utcNow, TimeSpan staleAfter, string reason, CancellationToken ct)
        {
            var staleBefore = utcNow.Subtract(staleAfter);

            return await _db.Database.ExecuteSqlRawAsync(@"
UPDATE dbo.OutboxMessages
SET
    LockId = NULL,
    LockedAt = NULL,
    Error = {0}
WHERE
    Status = {1}
    AND LockId IS NOT NULL
    AND LockedAt IS NOT NULL
    AND LockedAt < {2};
",
            new object[]
            {
                reason,
                (byte)OutboxStatus.Pending,
                staleBefore
            }, ct);
        }

        public async Task<int> ClaimBatchAsync(Guid lockId, DateTime utcNow, int batchSize, int maxAttempts, CancellationToken ct)
        {
            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = @"
UPDATE TOP (@BatchSize) o
SET
    o.Status = @ProcessingStatus,
    o.ProcessingStartedAt = @UtcNow,
    o.LockId = @LockId,
    o.LockedAt = @UtcNow
FROM dbo.OutboxMessages o WITH (ROWLOCK, READPAST, UPDLOCK)
WHERE
    o.Status = @PendingStatus
    AND o.PublishAttempts < @MaxAttempts
    AND o.LockId IS NULL;
SELECT @@ROWCOUNT;
";

            cmd.Parameters.Add(new SqlParameter("@BatchSize", SqlDbType.Int) { Value = batchSize });
            cmd.Parameters.Add(new SqlParameter("@UtcNow", SqlDbType.DateTime2) { Value = utcNow });
            cmd.Parameters.Add(new SqlParameter("@LockId", SqlDbType.UniqueIdentifier) { Value = lockId });
            cmd.Parameters.Add(new SqlParameter("@MaxAttempts", SqlDbType.Int) { Value = maxAttempts });
            cmd.Parameters.Add(new SqlParameter("@PendingStatus", SqlDbType.TinyInt) { Value = (byte)OutboxStatus.Pending });
            cmd.Parameters.Add(new SqlParameter("@ProcessingStatus", SqlDbType.TinyInt) { Value = (byte)OutboxStatus.Processing });

            var result = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt32(result);
        }

        public Task<List<OutboxMessage>> GetClaimedBatchAsync(Guid lockId, int batchSize, CancellationToken ct)
        {
            return _db.OutboxMessages
                .Where(x => x.Status == OutboxStatus.Processing && x.LockId == lockId)
                .OrderBy(x => x.CreatedAt)
                .Take(batchSize)
                .ToListAsync(ct);
        }
    }
}