using HUBDTE.Application.Azurian;
using HUBDTE.Infrastructure.Azurian.Layouts;

namespace HUBDTE.WorkerHost.HostedServices;

public sealed class AzurianLayoutStartupValidator : IHostedService
{
    private static readonly int[] RequiredTipos = { 33, 34, 39, 52, 56, 61, 110, 111, 112 };

    private readonly IAzurianLayoutRepository _repo;
    private readonly ILogger<AzurianLayoutStartupValidator> _logger;

    // Campos permitidos como "calculados" (no requieren FieldMap)
    private static readonly HashSet<string> CalculatedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "LineNo"
    };

    public AzurianLayoutStartupValidator(
        IAzurianLayoutRepository repo,
        ILogger<AzurianLayoutStartupValidator> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        foreach (var tipo in RequiredTipos)
        {
            AzurianTipoLayout layout;
            try
            {
                layout = _repo.Get(tipo);
            }
            catch (Exception ex)
            {
                errors.Add($"Falta layout para tipoDte={tipo}. Detalle: {ex.Message}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(layout.HeaderLineName))
                errors.Add($"tipoDte={tipo}: HeaderLineName vacío");

            if (string.IsNullOrWhiteSpace(layout.DetailLineName))
                errors.Add($"tipoDte={tipo}: DetailLineName vacío");

            if (layout.HeaderFields is null || layout.HeaderFields.Count == 0)
                errors.Add($"tipoDte={tipo}: HeaderFields vacío");

            if (layout.DetailFields is null || layout.DetailFields.Count == 0)
                errors.Add($"tipoDte={tipo}: DetailFields vacío");

            // Validar Fields (Name y Length > 0)
            foreach (var f in layout.HeaderFields ?? new List<FixedWidthFieldConfig>())
            {
                if (string.IsNullOrWhiteSpace(f.Name))
                    errors.Add($"tipoDte={tipo}: HeaderFields tiene Name vacío");
                if (f.Length <= 0)
                    errors.Add($"tipoDte={tipo}: HeaderField '{f.Name}' Length inválido ({f.Length})");
            }

            foreach (var f in layout.DetailFields ?? new List<FixedWidthFieldConfig>())
            {
                if (string.IsNullOrWhiteSpace(f.Name))
                    errors.Add($"tipoDte={tipo}: DetailFields tiene Name vacío");
                if (f.Length <= 0)
                    errors.Add($"tipoDte={tipo}: DetailField '{f.Name}' Length inválido ({f.Length})");
            }

            var headerMap = layout.HeaderMap ?? new List<FieldMapConfig>();
            var detailMap = layout.DetailMap ?? new List<FieldMapConfig>();

            var headerMapNames = new HashSet<string>(
                headerMap.Select(x => x.FieldName).Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);

            var detailMapNames = new HashSet<string>(
                detailMap.Select(x => x.FieldName).Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var f in layout.HeaderFields ?? new List<FixedWidthFieldConfig>())
            {
                if (!headerMapNames.Contains(f.Name) && !CalculatedFields.Contains(f.Name))
                    errors.Add($"tipoDte={tipo}: HeaderField '{f.Name}' no tiene HeaderMap y no es calculado");
            }

            foreach (var f in layout.DetailFields ?? new List<FixedWidthFieldConfig>())
            {
                if (!detailMapNames.Contains(f.Name) && !CalculatedFields.Contains(f.Name))
                    errors.Add($"tipoDte={tipo}: DetailField '{f.Name}' no tiene DetailMap y no es calculado");
            }

            // Validar maps: FieldName + JsonPath
            // Permitimos JsonPath vacío cuando Transform = Literal (y también Default si quieres)
            foreach (var m in headerMap)
            {
                if (string.IsNullOrWhiteSpace(m.FieldName))
                    errors.Add($"tipoDte={tipo}: HeaderMap tiene FieldName vacío");

                var needsJsonPath = m.Transform != FieldTransform.Literal; // ajusta si agregas más transforms sin JsonPath
                if (needsJsonPath && string.IsNullOrWhiteSpace(m.JsonPath))
                    errors.Add($"tipoDte={tipo}: HeaderMap '{m.FieldName}' tiene JsonPath vacío");
            }

            foreach (var m in detailMap)
            {
                if (string.IsNullOrWhiteSpace(m.FieldName))
                    errors.Add($"tipoDte={tipo}: DetailMap tiene FieldName vacío");

                var needsJsonPath = m.Transform != FieldTransform.Literal; // ajusta si agregas más transforms sin JsonPath
                if (needsJsonPath && string.IsNullOrWhiteSpace(m.JsonPath))
                    errors.Add($"tipoDte={tipo}: DetailMap '{m.FieldName}' tiene JsonPath vacío");
            }
        }

        if (errors.Count > 0)
        {
            var msg = "❌ AzurianLayout inválido:\n- " + string.Join("\n- ", errors);
            _logger.LogError(msg);
            throw new InvalidOperationException(msg);
        }

        _logger.LogInformation("✅ AzurianLayout validado OK para tipos: {Tipos}", string.Join(",", RequiredTipos));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}