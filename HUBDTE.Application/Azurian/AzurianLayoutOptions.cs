namespace HUBDTE.Application.Azurian;

public sealed class AzurianLayoutOptions
{
    // ✅ Constantes globales (se mezclan con las del archivo por tipo)
    public Dictionary<string, string> Constants { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ✅ Carpeta relativa al bin (AppContext.BaseDirectory) donde están los json por tipo
    public string LayoutsPath { get; set; } = "AzurianLayouts";

    // (opcional) si sigues soportando "Tipos" en appsettings grande, puedes dejarlo:
    public Dictionary<int, AzurianTipoLayout> Tipos { get; set; } = new();
}

public sealed class AzurianTipoLayout
{
    public string HeaderLineName { get; set; } = default!;
    public string DetailLineName { get; set; } = default!;
    public List<FixedWidthFieldConfig> HeaderFields { get; set; } = [];
    public List<FixedWidthFieldConfig> DetailFields { get; set; } = [];
    public List<FieldMapConfig> HeaderMap { get; set; } = [];
    public List<FieldMapConfig> DetailMap { get; set; } = [];
    public List<GlosaConfig> Glosas { get; set; } = [];
}

public sealed class GlosaConfig
{
    public int Seq { get; set; }
    public string Code { get; set; } = "";

    public bool? Enabled { get; set; } = null;     // 👈 nullable
    public bool? OmitIfEmpty { get; set; } = null; // 👈 nullable

    public string? ValueLiteral { get; set; }
    public string? ValueConstantKey { get; set; }
    public string? ValueJsonPath { get; set; }
    public List<AzurianGlosaPartConfig>? Parts { get; set; }
}

public sealed class AzurianGlosaPartConfig
{
    public string? ValueLiteral { get; set; }
    public string? ValueConstantKey { get; set; }
    public string? ValueJsonPath { get; set; }
}

public sealed class FixedWidthFieldConfig
{
    public string Name { get; set; } = default!;
    public int Length { get; set; }
    public char PadChar { get; set; } = ' ';
    public bool PadLeft { get; set; } = false;
    public bool Trim { get; set; } = true;
}

// ✅ CAMBIO AQUÍ
public sealed class FieldMapConfig
{
    public string FieldName { get; set; } = default!;
    public string JsonPath { get; set; } = "";               // puede ser vacío para Literal/Default
    public FieldTransform Transform { get; set; } = FieldTransform.None;

    // Antes era int? -> ahora string? para soportar "E", "00000", etc.
    public string? TransformArg { get; set; } = null;
}
