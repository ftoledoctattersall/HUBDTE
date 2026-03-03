using HUBDTE.Application.Azurian;

namespace HUBDTE.Infrastructure.Azurian.Layouts;

internal sealed class AzurianTipoLayoutFile
{
    public Dictionary<string, string>? Constants { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public AzurianTipoLayout? Layout { get; set; }
}