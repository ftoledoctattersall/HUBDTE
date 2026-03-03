namespace HUBDTE.Application.Azurian;

public enum FieldTransform
{
    None,
    Trim,
    Upper,
    Lower,
    OnlyDigits,
    ZeroPadLeft,
    SpacePadRight,
    Date_yyyyMMdd,

    // ✅ nuevos
    Literal,   // ignora JSON y devuelve TransformArg tal cual
    Default    // si value es null/vacío => usa TransformArg
}


public sealed record FieldMap(
    string FieldName,
    string? JsonPath,
    FieldTransform Transform,
    string? TransformArg // <- antes era int?
);

