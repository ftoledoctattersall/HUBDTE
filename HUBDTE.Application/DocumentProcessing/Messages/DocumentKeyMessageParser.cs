using System.Text.Json;

namespace HUBDTE.Application.DocumentProcessing.Messages;

public static class DocumentKeyMessageParser
{
    public static bool TryParse(string json, out DocumentKeyMessage message, out string error)
    {
        message = default!;
        error = "";

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("filialCode", out var filialProp) || filialProp.ValueKind != JsonValueKind.String)
            {
                error = "Falta filialCode (string).";
                return false;
            }

            var filial = filialProp.GetString();
            if (string.IsNullOrWhiteSpace(filial))
            {
                error = "filialCode viene vacío.";
                return false;
            }

            if (!TryGetLong(root, "docEntry", out var docEntry))
            {
                error = "Falta docEntry o no es numérico.";
                return false;
            }

            if (!TryGetInt(root, "tipoDte", out var tipoDte))
            {
                error = "Falta tipoDte o no es numérico.";
                return false;
            }

            message = new DocumentKeyMessage(filial!, docEntry, tipoDte);
            return true;
        }
        catch (Exception ex)
        {
            error = $"JSON inválido: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetLong(JsonElement obj, string prop, out long value)
    {
        value = 0;
        if (!obj.TryGetProperty(prop, out var v)) return false;

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out value)) return true;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out value)) return true;

        return false;
    }

    private static bool TryGetInt(JsonElement obj, string prop, out int value)
    {
        value = 0;
        if (!obj.TryGetProperty(prop, out var v)) return false;

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out value)) return true;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out value)) return true;

        return false;
    }
}