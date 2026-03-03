namespace HUBDTE.Domain.Entities
{
    public class OutboxMessage
    {
        public Guid Id { get; set; }

        public Guid SapDocumentId { get; set; }
        public SapDocument SapDocument { get; set; } = default!;

        public string MessageType { get; set; } = default!;
        public string Body { get; set; } = default!;

        public OutboxStatus Status { get; set; }
        public int PublishAttempts { get; set; }

        public DateTime? ProcessingStartedAt { get; set; }
        public DateTime? PublishedAt { get; set; }

        // ✅ Locking columns (claim)
        public Guid? LockId { get; set; }
        public DateTime? LockedAt { get; set; }

        public string? Error { get; set; }
        public DateTime CreatedAt { get; set; }

        private const int MaxErrorLength = 2000;

        public string? CorrelationId { get; set; }
        public string? MessageTypeHeader { get; set; }

        // =========================
        // Domain behavior
        // =========================

        public void Claim(Guid lockId, DateTime utcNow)
        {
            if (Status != OutboxStatus.Pending)
                throw new InvalidOperationException($"No se puede claimear si no está Pending. Status={Status}");

            if (LockId is not null)
                throw new InvalidOperationException("No se puede claimear un OutboxMessage que ya tiene LockId.");

            LockId = lockId;
            LockedAt = utcNow;
        }

        public void ReleaseLock(string? reason = null)
        {
            LockId = null;
            LockedAt = null;
            if (!string.IsNullOrWhiteSpace(reason))
                Error = TrimError(reason);
        }

        public void MarkProcessing()
        {
            if (LockId == null)
                throw new InvalidOperationException("No se puede pasar a Processing sin LockId.");

            EnsureNotTerminal(nameof(MarkProcessing));

            if (Status != OutboxStatus.Pending && Status != OutboxStatus.Processing)
                throw new InvalidOperationException($"Transición inválida: {Status} -> Processing.");

            Status = OutboxStatus.Processing;

            ProcessingStartedAt ??= DateTime.UtcNow;

            // ✅ Importantísimo: ya no se necesita lock, porque ya está en Processing
            ReleaseLock();
        }

        public void MarkPublished()
        {
            if (Status != OutboxStatus.Processing)
                throw new InvalidOperationException($"Transición inválida: {Status} -> Published. Debe venir desde Processing.");

            Status = OutboxStatus.Published;
            PublishedAt = DateTime.UtcNow;

            ProcessingStartedAt = null;
            Error = null;

            // ✅ limpieza final
            ReleaseLock();
        }

        public void MarkRetryableFailure(Exception ex)
        {
            if (ex is null) throw new ArgumentNullException(nameof(ex));
            MarkRetryableFailure(ex.Message);
        }

        public void MarkRetryableFailure(string error)
        {
            EnsureNotTerminal(nameof(MarkRetryableFailure));

            PublishAttempts += 1;
            Status = OutboxStatus.Pending;

            ProcessingStartedAt = null;
            PublishedAt = null;

            // ✅ si vuelve a Pending, DEBE quedar claimable
            ReleaseLock();
            Error = TrimError(error);
        }

        public void MarkFailed(Exception ex)
        {
            if (ex is null) throw new ArgumentNullException(nameof(ex));
            MarkFailed(ex.Message);
        }

        public void MarkFailed(string error)
        {
            EnsureNotTerminal(nameof(MarkFailed));

            PublishAttempts += 1;
            Status = OutboxStatus.Failed;

            ProcessingStartedAt = null;

            // ✅ limpieza final
            ReleaseLock();
            Error = TrimError(error);
        }

        public bool IsStuckProcessing(DateTime utcNow, TimeSpan stuckAfter)
        {
            if (Status != OutboxStatus.Processing) return false;
            if (PublishedAt is not null) return false;

            return ProcessingStartedAt.HasValue &&
                   ProcessingStartedAt.Value < utcNow.Subtract(stuckAfter);
        }

        public void RescueStuckProcessing(string reason)
        {
            if (Status != OutboxStatus.Processing)
                return;

            Status = OutboxStatus.Pending;
            ProcessingStartedAt = null;

            // ✅ vuelve claimable
            ReleaseLock();
            Error = TrimError(reason);
        }

        private void EnsureNotTerminal(string op)
        {
            if (Status is OutboxStatus.Published or OutboxStatus.Failed)
                throw new InvalidOperationException(
                    $"No se puede ejecutar {op} porque el OutboxMessage está en estado terminal ({Status}).");
        }

        private static string? TrimError(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var s = value.Trim();
            if (s.Length <= MaxErrorLength) return s;

            return s.Substring(s.Length - MaxErrorLength, MaxErrorLength);
        }
    }
}