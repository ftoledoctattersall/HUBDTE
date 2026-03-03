namespace HUBDTE.Application.Azurian;

public interface IAzurianClient
{
    Task<AzurianProcessResult> SendTxtAsync(
        string txtContent,
        string fileName,
        string? empresa,
        CancellationToken ct);
}

public sealed class AzurianProcessResult
{
    public bool Ok { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
}