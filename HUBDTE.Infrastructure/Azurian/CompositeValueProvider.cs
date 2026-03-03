using HUBDTE.Application.Azurian;

namespace HUBDTE.Infrastructure.Azurian;

public sealed class CompositeValueProvider : IFixedWidthValueProvider
{
    private readonly IReadOnlyDictionary<string, string?> _overrides;
    private readonly IFixedWidthValueProvider _fallback;

    public CompositeValueProvider(
        IReadOnlyDictionary<string, string?> overrides,
        IFixedWidthValueProvider fallback)
    {
        _overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public string? GetValue(string fieldName)
    {
        if (_overrides.TryGetValue(fieldName, out var value))
            return value;

        return _fallback.GetValue(fieldName);
    }
}