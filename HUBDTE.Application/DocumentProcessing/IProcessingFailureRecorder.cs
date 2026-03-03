namespace HUBDTE.Application.DocumentProcessing;

public interface IProcessingFailureRecorder
{
    Task RecordFailureAsync(
        string filialCode,
        long docEntry,
        int tipoDte,
        int attempt,
        Exception ex,
        string? correlationId,
        CancellationToken ct);
}