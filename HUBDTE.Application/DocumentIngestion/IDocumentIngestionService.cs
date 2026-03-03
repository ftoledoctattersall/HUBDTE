using System.Text.Json;

namespace HUBDTE.Application.DocumentIngestion;

public interface IDocumentIngestionService
{
    Task<IngestResult> IngestAsync(JsonElement payload, string? clientToken, CancellationToken ct);
}

public sealed record IngestResult(int HttpStatus, object Body);
