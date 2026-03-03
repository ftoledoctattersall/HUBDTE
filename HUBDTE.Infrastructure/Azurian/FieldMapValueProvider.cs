using HUBDTE.Application.Azurian;
using System.Globalization;
using System.Text.Json;

namespace HUBDTE.Infrastructure.Azurian;

public sealed class FieldMapValueProvider : IFixedWidthValueProvider
{
    private readonly JsonElement _root;
    private readonly IReadOnlyDictionary<string, FieldMap> _map;
    private readonly IReadOnlyDictionary<string, string> _constants;
    private readonly int? _arrayIndex;

    public FieldMapValueProvider(
        JsonElement root,
        IEnumerable<FieldMap> map,
        IReadOnlyDictionary<string, string>? constants = null,
        int? arrayIndex = null)
    {
        _root = root;
        _map = map.ToDictionary(x => x.FieldName, x => x, StringComparer.OrdinalIgnoreCase);
        _arrayIndex = arrayIndex;
        _constants = constants ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public string? GetValue(string fieldName)
    {
        if (!_map.TryGetValue(fieldName, out var fm))
            return null;

        if (fm.Transform == FieldTransform.Literal)
            return ResolveLiteral(fm.TransformArg);

        var raw = string.IsNullOrWhiteSpace(fm.JsonPath)
            ? null
            : JsonPathValueResolver.GetString(_root, fm.JsonPath, _arrayIndex);

        if (fm.Transform == FieldTransform.Default)
            return string.IsNullOrWhiteSpace(raw) ? (fm.TransformArg ?? "") : raw;

        return ApplyTransform(raw, fm.Transform, fm.TransformArg);
    }

    private string ResolveLiteral(string? keyOrValue)
    {
        if (string.IsNullOrWhiteSpace(keyOrValue))
            return "";

        if (_constants.TryGetValue(keyOrValue, out var constValue))
            return constValue;

        return keyOrValue;
    }

    private string? ApplyTransform(string? value, FieldTransform transform, string? arg)
    {
        if (value is null)
            return null;

        switch (transform)
        {
            case FieldTransform.None:
                return value;

            case FieldTransform.Trim:
                return value.Trim();

            case FieldTransform.Upper:
                return value.Trim().ToUpperInvariant();

            case FieldTransform.Lower:
                return value.Trim().ToLowerInvariant();

            case FieldTransform.OnlyDigits:
                return new string(value.Where(char.IsDigit).ToArray());

            case FieldTransform.ZeroPadLeft:
                {
                    var v = value.Trim();
                    var len = int.TryParse(arg, out var n) ? n : v.Length;
                    return v.PadLeft(len, '0');
                }

            case FieldTransform.SpacePadRight:
                {
                    var v = value.Trim();
                    var len = int.TryParse(arg, out var n) ? n : v.Length;
                    return v.PadRight(len, ' ');
                }

            case FieldTransform.Date_yyyyMMdd:
                {
                    var v = value.Trim();

                    if (v.Length == 8 && v.All(char.IsDigit))
                        return v;

                    if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt) ||
                        DateTime.TryParse(v, CultureInfo.GetCultureInfo("es-CL"), DateTimeStyles.AssumeLocal, out dt))
                    {
                        return dt.ToString("yyyyMMdd");
                    }

                    return v;
                }

            case FieldTransform.Literal:
                return ResolveLiteral(arg);

            case FieldTransform.Default:
                return string.IsNullOrWhiteSpace(value) ? (arg ?? "") : value;

            default:
                return value;
        }
    }
}