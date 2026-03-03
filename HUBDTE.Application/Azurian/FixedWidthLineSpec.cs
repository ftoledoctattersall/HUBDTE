namespace HUBDTE.Application.Azurian;
public sealed class FixedWidthLineSpec
{
    public required string LineName { get; init; }
    public required IReadOnlyList<FixedWidthField> Fields { get; init; }
}
