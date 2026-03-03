public sealed class AzurianOutputDirStartupValidator : IHostedService
{
    private readonly IConfiguration _config;
    private readonly ILogger<AzurianOutputDirStartupValidator> _logger;

    public AzurianOutputDirStartupValidator(IConfiguration config, ILogger<AzurianOutputDirStartupValidator> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var dir = _config.GetSection("AzurianOutput")["Directory"];
        if (string.IsNullOrWhiteSpace(dir))
            throw new InvalidOperationException("Falta AzurianOutput:Directory");

        Directory.CreateDirectory(dir);

        // test de escritura
        var testFile = Path.Combine(dir, $"_write_test_{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(testFile, "ok", cancellationToken);
        File.Delete(testFile);

        _logger.LogInformation("✅ Carpeta de salida Azurian OK: {Dir}", dir);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
