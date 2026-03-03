using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using HUBDTE.Application.Azurian;
using Microsoft.Extensions.Options;

namespace HUBDTE.Infrastructure.Azurian.Layouts;

public sealed class AzurianLayoutRepository : IAzurianLayoutRepository
{
    private readonly AzurianLayoutOptions _options;
    private readonly string _layoutsRoot;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ConcurrentDictionary<string, Lazy<(AzurianTipoLayout Layout, IReadOnlyDictionary<string, string> Consts)>> _cache = new();

    public AzurianLayoutRepository(IOptions<AzurianLayoutOptions> options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        _options = options.Value ?? throw new InvalidOperationException("AzurianLayoutOptions no puede ser null.");

        var layoutsPath = string.IsNullOrWhiteSpace(_options.LayoutsPath)
            ? "AzurianLayouts"
            : _options.LayoutsPath;

        _layoutsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, layoutsPath));
    }

    public AzurianTipoLayout Get(int tipoDte, string? empresa = null)
        => GetEntry(tipoDte, empresa).Layout;

    public IReadOnlyDictionary<string, string> GetConstants(int tipoDte, string? empresa = null)
        => GetEntry(tipoDte, empresa).Consts;

    private (AzurianTipoLayout Layout, IReadOnlyDictionary<string, string> Consts) GetEntry(int tipoDte, string? empresa)
    {
        var key = MakeCacheKey(tipoDte, empresa);

        var lazy = _cache.GetOrAdd(
            key,
            _ => new Lazy<(AzurianTipoLayout, IReadOnlyDictionary<string, string>)>(
                () => LoadMerged(tipoDte, empresa),
                isThreadSafe: true));

        return lazy.Value;
    }

    private (AzurianTipoLayout Layout, IReadOnlyDictionary<string, string> Consts) LoadMerged(int tipoDte, string? empresa)
    {
        var empresaNormalizada = NormalizeEmpresa(empresa);

        var baseFile = TryLoadLayoutFile("base.json");
        var tipoFile = TryLoadLayoutFile($"tipo.{tipoDte}.json");
        var tipoEmpresaFile = string.IsNullOrWhiteSpace(empresaNormalizada)
            ? null
            : TryLoadLayoutFile($"tipo.{tipoDte}.emp.{empresaNormalizada}.json");

        var mergedConsts = BuildMergedConstants(baseFile, tipoFile, tipoEmpresaFile);
        var mergedLayout = BuildMergedLayout(tipoDte, empresa, baseFile, tipoFile, tipoEmpresaFile);

        ValidateMergedLayout(tipoDte, empresa, mergedLayout);

        return (mergedLayout, mergedConsts);
    }

    private IReadOnlyDictionary<string, string> BuildMergedConstants(
        AzurianTipoLayoutFile? baseFile,
        AzurianTipoLayoutFile? tipoFile,
        AzurianTipoLayoutFile? tipoEmpresaFile)
    {
        var mergedConsts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_options.Constants is not null)
        {
            foreach (var kv in _options.Constants)
                mergedConsts[kv.Key] = kv.Value;
        }

        MergeConstants(mergedConsts, baseFile?.Constants);
        MergeConstants(mergedConsts, tipoFile?.Constants);
        MergeConstants(mergedConsts, tipoEmpresaFile?.Constants);

        return mergedConsts;
    }

    private static AzurianTipoLayout BuildMergedLayout(
        int tipoDte,
        string? empresa,
        AzurianTipoLayoutFile? baseFile,
        AzurianTipoLayoutFile? tipoFile,
        AzurianTipoLayoutFile? tipoEmpresaFile)
    {
        AzurianTipoLayout? mergedLayout = null;

        if (baseFile?.Layout is not null)
            mergedLayout = CloneLayout(baseFile.Layout);

        if (mergedLayout is null)
        {
            if (tipoFile?.Layout is null)
            {
                throw new InvalidOperationException(
                    $"No existe layout para tipoDte={tipoDte}. Debe existir 'tipo.{tipoDte}.json' o 'base.json' + overrides.");
            }

            mergedLayout = CloneLayout(tipoFile.Layout);
        }
        else if (tipoFile?.Layout is not null)
        {
            mergedLayout = MergeLayout(mergedLayout, tipoFile.Layout);
        }

        if (tipoEmpresaFile?.Layout is not null)
            mergedLayout = MergeLayout(mergedLayout, tipoEmpresaFile.Layout);

        return mergedLayout;
    }

    private static void ValidateMergedLayout(int tipoDte, string? empresa, AzurianTipoLayout layout)
    {
        if (string.IsNullOrWhiteSpace(layout.HeaderLineName))
            throw new InvalidOperationException($"Layout final tipoDte={tipoDte}, empresa={empresa}: HeaderLineName vacío");

        if (string.IsNullOrWhiteSpace(layout.DetailLineName))
            throw new InvalidOperationException($"Layout final tipoDte={tipoDte}, empresa={empresa}: DetailLineName vacío");

        if (layout.HeaderFields is null || layout.HeaderFields.Count == 0)
            throw new InvalidOperationException($"Layout final tipoDte={tipoDte}, empresa={empresa}: HeaderFields vacío");

        if (layout.DetailFields is null || layout.DetailFields.Count == 0)
            throw new InvalidOperationException($"Layout final tipoDte={tipoDte}, empresa={empresa}: DetailFields vacío");
    }

    private AzurianTipoLayoutFile? TryLoadLayoutFile(string fileName)
    {
        var fullPath = Path.Combine(_layoutsRoot, fileName);

        if (!File.Exists(fullPath))
            return null;

        var json = File.ReadAllText(fullPath);

        var file = JsonSerializer.Deserialize<AzurianTipoLayoutFile>(json, JsonOpts);

        if (file is null)
            throw new InvalidOperationException($"No se pudo deserializar layout file: {fullPath}");

        file.Constants ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (file.Layout is not null)
        {
            file.Layout.HeaderFields ??= [];
            file.Layout.DetailFields ??= [];
            file.Layout.HeaderMap ??= [];
            file.Layout.DetailMap ??= [];
            file.Layout.Glosas ??= [];
        }

        return file;
    }

    private static void MergeConstants(Dictionary<string, string> target, Dictionary<string, string>? source)
    {
        if (source is null) return;

        foreach (var kv in source)
            target[kv.Key] = kv.Value;
    }

    private static string MakeCacheKey(int tipoDte, string? empresa)
        => $"{tipoDte}:{NormalizeEmpresa(empresa) ?? ""}".ToUpperInvariant();

    private static string? NormalizeEmpresa(string? empresa)
    {
        if (string.IsNullOrWhiteSpace(empresa))
            return null;

        return empresa.Trim().Replace(" ", "");
    }

    private static AzurianTipoLayout MergeLayout(AzurianTipoLayout current, AzurianTipoLayout overrideLayout)
    {
        if (!string.IsNullOrWhiteSpace(overrideLayout.HeaderLineName))
            current.HeaderLineName = overrideLayout.HeaderLineName;

        if (!string.IsNullOrWhiteSpace(overrideLayout.DetailLineName))
            current.DetailLineName = overrideLayout.DetailLineName;

        current.HeaderFields = MergeFieldsByName(current.HeaderFields, overrideLayout.HeaderFields);
        current.DetailFields = MergeFieldsByName(current.DetailFields, overrideLayout.DetailFields);

        current.HeaderMap = MergeMapsByFieldName(current.HeaderMap, overrideLayout.HeaderMap);
        current.DetailMap = MergeMapsByFieldName(current.DetailMap, overrideLayout.DetailMap);

        current.Glosas = MergeGlosasBySeq(current.Glosas, overrideLayout.Glosas);

        return current;
    }

    private static List<GlosaConfig> MergeGlosasBySeq(List<GlosaConfig>? baseList, List<GlosaConfig>? overrideList)
    {
        baseList ??= [];
        overrideList ??= [];

        var dict = new Dictionary<int, GlosaConfig>();

        foreach (var g in baseList)
            dict[g.Seq] = CloneGlosa(g);

        foreach (var ov in overrideList)
        {
            if (!dict.TryGetValue(ov.Seq, out var current))
            {
                dict[ov.Seq] = CloneGlosa(ov);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ov.Code))
                current.Code = ov.Code;

            if (ov.Enabled is not null)
                current.Enabled = ov.Enabled;

            if (ov.OmitIfEmpty is not null)
                current.OmitIfEmpty = ov.OmitIfEmpty;

            if (ov.Parts is { Count: > 0 })
                current.Parts = CloneGlosa(ov).Parts;

            if (ov.ValueLiteral is not null)
                current.ValueLiteral = ov.ValueLiteral;

            if (ov.ValueJsonPath is not null)
                current.ValueJsonPath = ov.ValueJsonPath;

            if (ov.ValueConstantKey is not null)
                current.ValueConstantKey = ov.ValueConstantKey;
        }

        return dict.Values.OrderBy(x => x.Seq).ToList();
    }

    private static List<FixedWidthFieldConfig> MergeFieldsByName(
        List<FixedWidthFieldConfig>? baseList,
        List<FixedWidthFieldConfig>? overrideList)
    {
        baseList ??= [];
        overrideList ??= [];

        var dict = new Dictionary<string, FixedWidthFieldConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in baseList)
        {
            if (string.IsNullOrWhiteSpace(field.Name)) continue;
            dict[field.Name] = CloneField(field);
        }

        foreach (var field in overrideList)
        {
            if (string.IsNullOrWhiteSpace(field.Name)) continue;
            dict[field.Name] = CloneField(field);
        }

        var result = new List<FixedWidthFieldConfig>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in baseList)
        {
            if (string.IsNullOrWhiteSpace(field.Name)) continue;
            if (dict.TryGetValue(field.Name, out var value) && added.Add(field.Name))
                result.Add(value);
        }

        foreach (var field in overrideList)
        {
            if (string.IsNullOrWhiteSpace(field.Name)) continue;
            if (dict.TryGetValue(field.Name, out var value) && added.Add(field.Name))
                result.Add(value);
        }

        return result;
    }

    private static List<FieldMapConfig> MergeMapsByFieldName(
        List<FieldMapConfig>? baseList,
        List<FieldMapConfig>? overrideList)
    {
        baseList ??= [];
        overrideList ??= [];

        var dict = new Dictionary<string, FieldMapConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var map in baseList)
        {
            if (string.IsNullOrWhiteSpace(map.FieldName)) continue;
            dict[map.FieldName] = CloneMap(map);
        }

        foreach (var map in overrideList)
        {
            if (string.IsNullOrWhiteSpace(map.FieldName)) continue;
            dict[map.FieldName] = CloneMap(map);
        }

        var result = new List<FieldMapConfig>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var map in baseList)
        {
            if (string.IsNullOrWhiteSpace(map.FieldName)) continue;
            if (dict.TryGetValue(map.FieldName, out var value) && added.Add(map.FieldName))
                result.Add(value);
        }

        foreach (var map in overrideList)
        {
            if (string.IsNullOrWhiteSpace(map.FieldName)) continue;
            if (dict.TryGetValue(map.FieldName, out var value) && added.Add(map.FieldName))
                result.Add(value);
        }

        return result;
    }

    private static AzurianTipoLayout CloneLayout(AzurianTipoLayout src)
    {
        return new AzurianTipoLayout
        {
            HeaderLineName = src.HeaderLineName,
            DetailLineName = src.DetailLineName,
            HeaderFields = (src.HeaderFields ?? []).Select(CloneField).ToList(),
            DetailFields = (src.DetailFields ?? []).Select(CloneField).ToList(),
            HeaderMap = (src.HeaderMap ?? []).Select(CloneMap).ToList(),
            DetailMap = (src.DetailMap ?? []).Select(CloneMap).ToList(),
            Glosas = (src.Glosas ?? []).Select(CloneGlosa).ToList()
        };
    }

    private static GlosaConfig CloneGlosa(GlosaConfig glosa)
    {
        return new GlosaConfig
        {
            Seq = glosa.Seq,
            Code = glosa.Code,
            ValueLiteral = glosa.ValueLiteral,
            ValueConstantKey = glosa.ValueConstantKey,
            ValueJsonPath = glosa.ValueJsonPath,
            OmitIfEmpty = glosa.OmitIfEmpty,
            Parts = glosa.Parts is null
                ? null
                : glosa.Parts.Select(CloneGlosaPart).ToList()
        };
    }

    private static AzurianGlosaPartConfig CloneGlosaPart(AzurianGlosaPartConfig part)
    {
        return new AzurianGlosaPartConfig
        {
            ValueLiteral = part.ValueLiteral,
            ValueConstantKey = part.ValueConstantKey,
            ValueJsonPath = part.ValueJsonPath
        };
    }

    private static FixedWidthFieldConfig CloneField(FixedWidthFieldConfig field)
    {
        return new FixedWidthFieldConfig
        {
            Name = field.Name,
            Length = field.Length,
            PadChar = field.PadChar,
            PadLeft = field.PadLeft,
            Trim = field.Trim
        };
    }

    private static FieldMapConfig CloneMap(FieldMapConfig map)
    {
        return new FieldMapConfig
        {
            FieldName = map.FieldName,
            JsonPath = map.JsonPath,
            Transform = map.Transform,
            TransformArg = map.TransformArg
        };
    }
}