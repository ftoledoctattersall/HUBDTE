using HUBDTE.Application.Interfaces.Persistence;
using Microsoft.Extensions.Logging;

namespace HUBDTE.Application.DocumentProcessing;

public sealed class ProcessingFailureRecorder : IProcessingFailureRecorder
{
    private readonly ISapDocumentRepository _sapDocs;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ProcessingFailureRecorder> _logger;

    public ProcessingFailureRecorder(
        ISapDocumentRepository sapDocs,
        IUnitOfWork uow,
        ILogger<ProcessingFailureRecorder> logger)
    {
        _sapDocs = sapDocs;
        _uow = uow;
        _logger = logger;
    }

    public async Task RecordFailureAsync(
        string filialCode,
        long docEntry,
        int tipoDte,
        int attempt,
        Exception ex,
        string? correlationId,
        CancellationToken ct)
    {
        try
        {
            var sapDoc = await _sapDocs.FindByKeyAsync(filialCode, docEntry, tipoDte, ct);
            if (sapDoc is null)
            {
                _logger.LogWarning(
                    "No se pudo registrar falla porque no existe SapDocument. Key={Filial}-{DocEntry}-{TipoDte}",
                    filialCode, docEntry, tipoDte);
                return;
            }

            sapDoc.MarkFailed(ex, attempt);

            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                sapDoc.ErrorReason = string.IsNullOrWhiteSpace(sapDoc.ErrorReason)
                    ? $"CorrelationId={correlationId}"
                    : $"{sapDoc.ErrorReason} | CorrelationId={correlationId}";
            }

            await _uow.SaveChangesAsync(ct);
        }
        catch (Exception recordEx)
        {
            _logger.LogError(recordEx,
                "Error registrando falla en DB para Key={Filial}-{DocEntry}-{TipoDte}",
                filialCode, docEntry, tipoDte);
        }
    }
}