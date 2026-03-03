using HUBDTE.Infrastructure.Azurian.Layouts;

namespace HUBDTE.Infrastructure.Azurian.Builders;

public sealed class AzurianTipoDteFixedWidthBuilder : BaseAzurianFixedWidthBuilder
{
    public AzurianTipoDteFixedWidthBuilder(
        IAzurianLayoutRepository repo,
        int tipoDte)
        : base(repo)
    {
        if (tipoDte <= 0)
            throw new ArgumentOutOfRangeException(nameof(tipoDte), "tipoDte debe ser mayor a 0.");

        TipoDte = tipoDte;
    }

    public override int TipoDte { get; }
}