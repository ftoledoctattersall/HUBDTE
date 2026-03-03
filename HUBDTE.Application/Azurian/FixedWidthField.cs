namespace HUBDTE.Application.Azurian;

public sealed record FixedWidthField(
    string Name,
    int Length,
    char PadChar = ' ',
    bool PadLeft = false,
    bool Trim = true
);
