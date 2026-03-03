using HUBDTE.Application.Azurian;

namespace HUBDTE.Infrastructure.Azurian.Layouts;

public interface IAzurianLayoutRepository
{
    AzurianTipoLayout Get(int tipoDte, string? empresa = null);
    IReadOnlyDictionary<string, string> GetConstants(int tipoDte, string? empresa = null);
}