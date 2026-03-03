using System.Text;
using HUBDTE.Application.Azurian;

namespace HUBDTE.Infrastructure.Azurian;

public static class FixedWidthRenderer
{
    public static string RenderLine(FixedWidthLineSpec spec, IFixedWidthValueProvider provider)
    {
        var sb = new StringBuilder();

        foreach (var field in spec.Fields)
        {
            var raw = provider.GetValue(field.Name) ?? string.Empty;

            if (field.Trim)
                raw = raw.Trim();

            if (raw.Length > field.Length)
                raw = raw[..field.Length];

            var padded = field.PadLeft
                ? raw.PadLeft(field.Length, field.PadChar)
                : raw.PadRight(field.Length, field.PadChar);

            sb.Append(padded);
        }

        return sb.ToString();
    }
}