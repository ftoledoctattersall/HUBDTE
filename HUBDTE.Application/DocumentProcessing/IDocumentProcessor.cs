namespace HUBDTE.Application.DocumentProcessing;
public interface IDocumentProcessor
{
    Task ProcessAsync(string filialCode, long docEntry, int tipoDte, int attempt, CancellationToken ct);
}