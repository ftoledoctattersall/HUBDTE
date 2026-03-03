using HUBDTE.Application.Interfaces;

namespace HUBDTE.Infrastructure.Azurian;

public sealed class AzurianTxtBuilder : IAzurianTxtBuilder
{
    private readonly IReadOnlyDictionary<int, IAzurianTipoDteTxtBuilder> _builders;

    public AzurianTxtBuilder(IEnumerable<IAzurianTipoDteTxtBuilder> builders)
    {
        if (builders is null) throw new ArgumentNullException(nameof(builders));

        var duplicated = builders
            .GroupBy(b => b.TipoDte)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicated.Length > 0)
        {
            throw new InvalidOperationException(
                $"Existen builders Azurian duplicados para tipoDte: {string.Join(", ", duplicated)}");
        }

        _builders = builders.ToDictionary(b => b.TipoDte, b => b);
    }

    public string BuildTxt(string payloadJson, int tipoDte)
    {
        if (!_builders.TryGetValue(tipoDte, out var builder))
            throw new InvalidOperationException($"No existe builder Azurian para tipoDte={tipoDte}");

        return builder.BuildTxt(payloadJson);
    }
}