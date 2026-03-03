using HUBDTE.Application.Azurian;
using HUBDTE.Application.Interfaces;
using HUBDTE.Application.Interfaces.Persistence;
using HUBDTE.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace HUBDTE.Application.DocumentProcessing;

public class DocumentProcessor : IDocumentProcessor
{
    private readonly ISapDocumentRepository _sapDocs;
    private readonly IUnitOfWork _uow;
    private readonly IAzurianTxtBuilder _txtBuilder;
    private readonly IAzurianClient _azurianClient;
    private readonly IAzurianFileWriter _fileWriter;
    private readonly IAzurianDevSettings _dev;
    private readonly ILogger<DocumentProcessor> _logger;

    public DocumentProcessor(
        ISapDocumentRepository sapDocs,
        IUnitOfWork uow,
        IAzurianTxtBuilder txtBuilder,
        IAzurianClient azurianClient,
        IAzurianFileWriter fileWriter,
        IAzurianDevSettings dev,
        ILogger<DocumentProcessor> logger)
    {
        _sapDocs = sapDocs;
        _uow = uow;
        _txtBuilder = txtBuilder;
        _azurianClient = azurianClient;
        _fileWriter = fileWriter;
        _dev = dev;
        _logger = logger;
    }

    //Punto despues de Consumir en RabbitConsumerWorker
    public async Task ProcessAsync(string filialCode, long docEntry, int tipoDte, int attempt, CancellationToken ct)
    {
        var sapDoc = await _sapDocs.FindByKeyAsync(filialCode, docEntry, tipoDte, ct);
        if (sapDoc is null)
        {
            _logger.LogWarning("No existe SapDocument para key {Filial}-{DocEntry}-{TipoDte}", filialCode, docEntry, tipoDte);
            return;
        }

        // ✅ Idempotencia fuerte
        if (sapDoc.Status == SapDocumentStatus.Processed)
        {
            _logger.LogInformation("Documento ya Processed. Se omite. {Filial}-{DocEntry}-{TipoDte}", filialCode, docEntry, tipoDte);
            return;
        }

        var claimed = await _sapDocs.TryClaimForProcessingAsync(filialCode, docEntry, tipoDte, ct);
        if (!claimed)
        {
            _logger.LogInformation(
                "Documento ya está siendo procesado o ya fue procesado. Se omite. {Filial}-{DocEntry}-{TipoDte}",
                filialCode, docEntry, tipoDte);
            return;
        }

        sapDoc.Status = SapDocumentStatus.Processing;

        try
        {
            var txt = _txtBuilder.BuildTxt(sapDoc.PayloadJson, tipoDte);
            var fileName = $"DTE_{filialCode}_{docEntry}_{tipoDte}.txt";

            if (_dev.ForceWriteTxt)
            {
                await _fileWriter.WriteTxtAsync(_dev.OutputPath, fileName, txt, ct);
                _logger.LogWarning("DEV MODE: TXT guardado. OutputPath={Path} File={File}", _dev.OutputPath, fileName);
            }

            var result = await _azurianClient.SendTxtAsync(
                txtContent: txt,
                fileName: fileName,
                empresa: filialCode,
                ct: ct);

            if (!result.Ok)
            {
                _logger.LogError("Azurian error {Code}: {Body}", result.StatusCode, result.ResponseBody);

                // DEV: no lanzamos excepción
                if (!_dev.ForceWriteTxt)
                    throw new Exception($"Azurian error {result.StatusCode}: {result.ResponseBody}");
            }

            sapDoc.MarkProcessed(result.ResponseBody ?? "");
            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation("Procesado OK. {Filial}-{DocEntry}-{TipoDte}", filialCode, docEntry, tipoDte);
        }
        catch (Exception ex)
        {
            sapDoc.MarkFailed(ex, attempt);
            await _uow.SaveChangesAsync(ct);

            _logger.LogError(ex, "Falló procesamiento. {Filial}-{DocEntry}-{TipoDte}", filialCode, docEntry, tipoDte);
            throw;
        }
    }
}