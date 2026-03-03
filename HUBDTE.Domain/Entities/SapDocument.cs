namespace HUBDTE.Domain.Entities
{
    public class SapDocument
    {
        public Guid Id { get; set; }

        public string FilialCode { get; set; } = default!;
        public long DocEntry { get; set; }
        public int TipoDte { get; set; }

        public string QueueName { get; set; } = default!;
        public string PayloadJson { get; set; } = default!;

        public SapDocumentStatus Status { get; set; }
        public int AttemptCount { get; set; }

        public DateTime? ProcessedAt { get; set; }
        public string? ErrorReason { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public ICollection<OutboxMessage> OutboxMessages { get; set; } = new List<OutboxMessage>();

        // =========================
        // Domain rules / behavior
        // =========================

        // Para evitar que ErrorReason crezca infinito en DB
        private const int MaxErrorReasonLength = 3500;

        public void MarkPending(string? queueName = null)
        {
            // ✅ Solo se permite:
            // - Desde Failed (reproceso)
            // - Desde Pending (idempotente)
            // - Desde Processing (reseteo controlado si tu sistema lo requiere)
            // ❌ Desde Processed: NO (idempotencia dura)
            EnsureNotProcessed(nameof(MarkPending));

            if (Status is not (SapDocumentStatus.Failed or SapDocumentStatus.Pending or SapDocumentStatus.Processing))
                throw new InvalidOperationException($"Transición inválida: {Status} -> Pending.");

            if (!string.IsNullOrWhiteSpace(queueName))
                QueueName = queueName;

            Status = SapDocumentStatus.Pending;
            AttemptCount = 0;
            ProcessedAt = null;
            ErrorReason = null;

            TouchUtcNow();
        }

        public void MarkProcessing()
        {
            // ✅ Permitido:
            // Pending -> Processing
            // Failed -> Processing (si decides procesar directo sin pasar por Pending)
            // ❌ Processed no puede volver
            EnsureNotProcessed(nameof(MarkProcessing));

            if (Status is not (SapDocumentStatus.Pending or SapDocumentStatus.Failed))
                throw new InvalidOperationException($"Transición inválida: {Status} -> Processing.");

            Status = SapDocumentStatus.Processing;
            TouchUtcNow();
        }

        public void MarkProcessed(string responseBody)
        {
            // ✅ Solo debe pasar desde Processing
            if (Status != SapDocumentStatus.Processing)
                throw new InvalidOperationException($"Transición inválida: {Status} -> Processed. Debe venir desde Processing.");

            Status = SapDocumentStatus.Processed;
            ProcessedAt = DateTime.UtcNow;

            // Si response viene null/vacío, igual marcamos OK pero sin ruido
            var body = string.IsNullOrWhiteSpace(responseBody) ? "OK" : responseBody.Trim();

            ErrorReason = $"OK - Azurian: {body}";
            TouchUtcNow();
        }

        public void MarkFailed(Exception ex, int attempt)
        {
            if (ex is null) throw new ArgumentNullException(nameof(ex));
            MarkFailedInternal($"{ex.GetType().Name}: {ex.Message}", attempt);
        }

        public void MarkFailed(string reason, int attempt)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("reason no puede ser vacío.", nameof(reason));

            MarkFailedInternal(reason.Trim(), attempt);
        }

        private void MarkFailedInternal(string reason, int attempt)
        {
            // ✅ Permitido:
            // Processing -> Failed
            // Pending -> Failed (por si falla antes de marcar Processing en algún flujo)
            // Failed -> Failed (idempotente / acumulación)
            // ❌ Processed no puede fallar
            EnsureNotProcessed(nameof(MarkFailed));

            if (Status is not (SapDocumentStatus.Processing or SapDocumentStatus.Pending or SapDocumentStatus.Failed))
                throw new InvalidOperationException($"Transición inválida: {Status} -> Failed.");

            AttemptCount += 1;
            Status = SapDocumentStatus.Failed;

            var entry = $"{DateTime.UtcNow:O} | Attempt={attempt} | {reason}";
            ErrorReason = AppendError(ErrorReason, entry);

            TouchUtcNow();
        }

        private static string AppendError(string? previous, string entry)
        {
            if (string.IsNullOrWhiteSpace(previous))
                return entry;

            return $"{previous} || {entry}";
        }

        private void EnsureNotProcessed(string operation)
        {
            if (Status == SapDocumentStatus.Processed)
                throw new InvalidOperationException($"No se puede ejecutar {operation} porque el documento ya está Processed.");
        }

        private void TouchUtcNow()
        {
            UpdatedAt = DateTime.UtcNow;

            // Evitar explosión de tamaño en DB (muy típico con retries)
            if (!string.IsNullOrWhiteSpace(ErrorReason) && ErrorReason.Length > MaxErrorReasonLength)
            {
                // Conserva el final (lo más reciente) que es lo que importa para debugging
                ErrorReason = ErrorReason.Substring(ErrorReason.Length - MaxErrorReasonLength, MaxErrorReasonLength);
            }
        }
    }
}