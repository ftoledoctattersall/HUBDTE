using System.Text.Json;

namespace HUBDTE.Infrastructure.Azurian;

public static class JsonPathValueResolver
{
    public static string? GetString(JsonElement root, string? path, int? arrayIndex = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JsonElement current = root;

        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();

            if (part.EndsWith("[]", StringComparison.Ordinal))
            {
                var arrayProp = part[..^2];

                if (!TryGetPropertyInsensitive(current, arrayProp, out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return null;

                if (arrayIndex is null) return null;

                var idx = arrayIndex.Value;
                if (idx < 0 || idx >= arr.GetArrayLength()) return null;

                current = arr[idx];
                continue;
            }

            if (!TryGetPropertyInsensitive(current, part, out var next))
                return null;

            current = next;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => current.ToString()
        };
    }

    public static string? GetStringProperty(JsonElement obj, string prop)
    {
        return TryGetPropertyInsensitive(obj, prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
    }

    public static int? GetIntProperty(JsonElement obj, string prop)
    {
        if (!TryGetPropertyInsensitive(obj, prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    public static long? GetLongProperty(JsonElement obj, string prop)
    {
        if (!TryGetPropertyInsensitive(obj, prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    public static bool TryGetArrayInsensitive(JsonElement root, string prop, out JsonElement value)
    {
        value = default;

        if (root.ValueKind != JsonValueKind.Object) return false;
        if (root.TryGetProperty(prop, out value)) return true;

        foreach (var p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, prop, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        return false;
    }

    public static bool TryGetPropertyInsensitive(JsonElement obj, string prop, out JsonElement value)
    {
        value = default;

        if (obj.ValueKind != JsonValueKind.Object)
            return false;

        if (obj.TryGetProperty(prop, out value))
            return true;

        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, prop, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        return false;
    }
}