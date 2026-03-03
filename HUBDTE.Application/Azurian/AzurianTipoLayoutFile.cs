using HUBDTE.Application.Azurian;

public sealed class AzurianTipoLayoutFile
{
    public Dictionary<string, string> Constants { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AzurianTipoLayout Layout { get; set; } = new();
}
