using HUBDTE.Application.Azurian;
using HUBDTE.Application.Interfaces;
using HUBDTE.Infrastructure.Azurian.Layouts;
using System.Text;
using System.Text.Json;

namespace HUBDTE.Infrastructure.Azurian.Builders;

public abstract class BaseAzurianFixedWidthBuilder : IAzurianTipoDteTxtBuilder
{
    private readonly IAzurianLayoutRepository _repo;

    protected BaseAzurianFixedWidthBuilder(IAzurianLayoutRepository repo)
    {
        _repo = repo;
    }

    public abstract int TipoDte { get; }

    protected virtual string DetailArrayPath => "detalle";

    public string BuildTxt(string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        var empresa = ResolveEmpresa(root);
        var layout = _repo.Get(TipoDte, empresa);
        var constants = _repo.GetConstants(TipoDte, empresa);

        var headerSpec = BuildLineSpec(layout.HeaderLineName, layout.HeaderFields);
        var detailSpec = BuildLineSpec(layout.DetailLineName, layout.DetailFields);

        var headerMap = BuildFieldMaps(layout.HeaderMap);
        var detailMap = BuildFieldMaps(layout.DetailMap);

        var sb = new StringBuilder();

        AppendHeader(sb, root, headerSpec, headerMap, constants);
        AppendRecargoIfAny(sb, root);
        AppendReferenciasF(sb, root);
        AppendDetails(sb, root, detailSpec, detailMap, constants);
        AppendGlosasG(sb, root, layout, constants);

        return sb.ToString();
    }

    private static string? ResolveEmpresa(JsonElement root)
    {
        return JsonPathValueResolver.GetString(root, "source.company.empresa")
            ?? JsonPathValueResolver.GetString(root, "source.company.filial")
            ?? JsonPathValueResolver.GetString(root, "source.company.filialCode");
    }

    private static FixedWidthLineSpec BuildLineSpec(
        string lineName,
        IEnumerable<FixedWidthFieldConfig> fields)
    {
        return new FixedWidthLineSpec
        {
            LineName = lineName,
            Fields = fields
                .Select(f => new FixedWidthField(f.Name, f.Length, f.PadChar, f.PadLeft, f.Trim))
                .ToArray()
        };
    }

    private static FieldMap[] BuildFieldMaps(IEnumerable<FieldMapConfig> maps)
    {
        return maps
            .Select(m => new FieldMap(m.FieldName, m.JsonPath, m.Transform, m.TransformArg))
            .ToArray();
    }

    private static void AppendHeader(
        StringBuilder sb,
        JsonElement root,
        FixedWidthLineSpec headerSpec,
        FieldMap[] headerMap,
        IReadOnlyDictionary<string, string?> constants)
    {
        var headerProvider = new FieldMapValueProvider(root, headerMap, constants);
        sb.AppendLine(FixedWidthRenderer.RenderLine(headerSpec, headerProvider));
    }

    private static void AppendRecargoIfAny(StringBuilder sb, JsonElement root)
    {
        var recargoStr = JsonPathValueResolver.GetString(root, "totales.recargo");

        if (TryGetRecargo(recargoStr, out var recargoDecimal) && recargoDecimal > 0)
        {
            sb.AppendLine(BuildRecargoLine(recargoDecimal));
        }
    }

    private void AppendDetails(
        StringBuilder sb,
        JsonElement root,
        FixedWidthLineSpec detailSpec,
        FieldMap[] detailMap,
        IReadOnlyDictionary<string, string?> constants)
    {
        if (!JsonPathValueResolver.TryGetArrayInsensitive(root, DetailArrayPath, out var detalle))
            return;

        if (detalle.ValueKind != JsonValueKind.Array)
            return;

        var total = detalle.GetArrayLength();

        for (var i = 0; i < total; i++)
        {
            var baseProvider = new FieldMapValueProvider(root, detailMap, constants, arrayIndex: i);

            var overrides = new Dictionary<string, string?>
            {
                ["LineNo"] = (i + 1).ToString()
            };

            var provider = new CompositeValueProvider(overrides, baseProvider);
            sb.AppendLine(FixedWidthRenderer.RenderLine(detailSpec, provider));
        }
    }

    private static bool TryGetRecargo(string? value, out decimal recargo)
    {
        recargo = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return decimal.TryParse(value, out recargo);
    }

    private static string BuildRecargoLine(decimal recargo)
    {
        const string desc = "Flete";

        var recargoStr = ((long)recargo).ToString();

        var sb = new StringBuilder();
        sb.Append("R");
        sb.Append(" 1");
        sb.Append("R");
        sb.Append(new string(' ', 45 - desc.Length) + desc);
        sb.Append("$");
        sb.Append(new string(' ', 18 - recargoStr.Length) + recargoStr);
        sb.Append("0");
        sb.Append(new string(' ', 18));

        return sb.ToString();
    }

    private static void AppendReferenciasF(StringBuilder sb, JsonElement root)
    {
        if (!JsonPathValueResolver.TryGetPropertyInsensitive(root, "referencias", out var refsEl))
            return;

        if (refsEl.ValueKind != JsonValueKind.Array)
            return;

        foreach (var r in refsEl.EnumerateArray())
        {
            var linea = JsonPathValueResolver.GetIntProperty(r, "linea") ?? 0;
            var tipoDoc = (JsonPathValueResolver.GetIntProperty(r, "tipoDocRef") ?? 0).ToString();
            var folio = (JsonPathValueResolver.GetLongProperty(r, "folioRef") ?? 0).ToString();
            var fecha = JsonPathValueResolver.GetStringProperty(r, "fechaRef") ?? "";

            sb.AppendLine(BuildReferenciaFLine(linea, tipoDoc, folio, fecha));
        }
    }

    private static string BuildGlosaGLine(int seq, string code, string value)
    {
        var sb = new StringBuilder();

        sb.Append("G");
        sb.Append(seq.ToString().PadLeft(2, ' '));

        code = (code ?? "").Trim();
        value ??= "";

        sb.Append(new string(' ', 20 - Math.Min(20, code.Length)) + code[..Math.Min(20, code.Length)]);
        sb.Append(new string(' ', 100 - Math.Min(100, value.Length)) + value[..Math.Min(100, value.Length)]);

        return sb.ToString();
    }

    private static string BuildReferenciaFLine(int linea, string tipoDocRef, string folioRef, string fechaRef)
    {
        var sb = new StringBuilder();

        linea = linea <= 0 ? 1 : linea;

        tipoDocRef = (tipoDocRef ?? "").Trim();
        folioRef = (folioRef ?? "").Trim();
        fechaRef = (fechaRef ?? "").Trim();

        var folioLargo = folioRef.Length > 10;

        sb.Append("F");
        sb.Append(" " + linea.ToString().Trim());
        sb.Append(PadLeftSpaces(tipoDocRef, 3));
        sb.Append("0");
        sb.Append(folioLargo ? new string(' ', 10) : PadLeftSpaces(folioRef, 10));
        sb.Append(new string(' ', 8));
        sb.Append(new string(' ', 1));
        sb.Append(PadLeftSpaces(fechaRef, 8));
        sb.Append(new string(' ', 1));
        sb.Append(new string(' ', 30));
        sb.Append(new string(' ', 10));
        sb.Append(new string(' ', 90));
        sb.Append(new string(' ', 1));
        sb.Append(folioLargo ? PadLeftSpaces(folioRef, 18) : new string(' ', 18));
        sb.Append(new string(' ', 20));
        sb.Append(new string(' ', 18));
        sb.Append(new string(' ', 8));
        sb.Append(new string(' ', 8));

        return sb.ToString();
    }

    private static void AppendGlosasG(
        StringBuilder sb,
        JsonElement root,
        AzurianTipoLayout layout,
        IReadOnlyDictionary<string, string?> constants)
    {
        if (layout.Glosas is null || layout.Glosas.Count == 0)
            return;

        foreach (var g in layout.Glosas
                     .Where(x => x.Enabled != false)
                     .OrderBy(x => x.Seq))
        {
            var value = ResolveGlosaValue(g, root, constants);

            var omitIfEmpty = g.OmitIfEmpty ?? false;
            if (omitIfEmpty && string.IsNullOrWhiteSpace(value))
                continue;

            sb.AppendLine(BuildGlosaGLine(g.Seq, g.Code, value));
        }
    }

    private static string ResolveGlosaValue(
        GlosaConfig g,
        JsonElement root,
        IReadOnlyDictionary<string, string?> constants)
    {
        if (g.Parts is { Count: > 0 })
        {
            var sb = new StringBuilder();
            foreach (var p in g.Parts)
                sb.Append(ResolveGlosaPart(p, root, constants));
            return sb.ToString();
        }

        if (!string.IsNullOrWhiteSpace(g.ValueLiteral))
            return g.ValueLiteral!;

        if (!string.IsNullOrWhiteSpace(g.ValueConstantKey))
            return constants.TryGetValue(g.ValueConstantKey, out var c) ? (c ?? "") : "";

        if (!string.IsNullOrWhiteSpace(g.ValueJsonPath))
            return JsonPathValueResolver.GetString(root, g.ValueJsonPath) ?? "";

        return "";
    }

    private static string ResolveGlosaPart(
        AzurianGlosaPartConfig p,
        JsonElement root,
        IReadOnlyDictionary<string, string?> constants)
    {
        if (!string.IsNullOrWhiteSpace(p.ValueLiteral))
            return p.ValueLiteral!;

        if (!string.IsNullOrWhiteSpace(p.ValueConstantKey))
            return constants.TryGetValue(p.ValueConstantKey, out var c) ? (c ?? "") : "";

        if (!string.IsNullOrWhiteSpace(p.ValueJsonPath))
            return JsonPathValueResolver.GetString(root, p.ValueJsonPath) ?? "";

        return "";
    }

    private static string PadLeftSpaces(string value, int len)
    {
        value ??= "";
        if (value.Length >= len) return value[..len];
        return new string(' ', len - value.Length) + value;
    }
}