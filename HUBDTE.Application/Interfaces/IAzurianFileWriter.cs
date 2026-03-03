namespace HUBDTE.Application.Interfaces;

public interface IAzurianFileWriter
{
    Task WriteTxtAsync(string outputDir, string fileName, string txtContent, CancellationToken ct);
}