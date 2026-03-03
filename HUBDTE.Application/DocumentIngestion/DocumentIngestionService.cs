using HUBDTE.Application.Interfaces.Persistence;
using HUBDTE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace HUBDTE.Application.DocumentIngestion;

public class DocumentIngestionService : IDocumentIngestionService
{
    private readonly ISapDocumentRepository _sapDocs;
    private readonly IOutboxMessageRepository _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IConfiguration _config;

    public DocumentIngestionService(
        ISapDocumentRepository sapDocs,
        IOutboxMessageRepository outbox,
        IUnitOfWork uow,
        IConfiguration config)
    {
        _sapDocs = sapDocs;
        _outbox = outbox;
        _uow = uow;
        _config = config;
    }

    private string ResolveQueueName(int tipoDte)
    {
        var key = MessageTypeFromTipoDte(tipoDte);

        var q = _config.GetSection("Queues").GetValue<string>(key);
        if (string.IsNullOrWhiteSpace(q))
            throw new InvalidOperationException($"Falta configuración: Queues:{key}");

        return q;
    }

    private static string MessageTypeFromTipoDte(int tipoDte) => tipoDte switch
    {
        39 => "Dte39",
        33 => "Dte33",
        34 => "Dte34",
        110 => "Dte110",
        61 => "Dte61",
        56 => "Dte56",
        111 => "Dte111",
        112 => "Dte112",
        52 => "Dte52",
        _ => throw new InvalidOperationException($"TipoDte no soportado: {tipoDte}")
    };

    public async Task<IngestResult> IngestAsync(JsonElement payload, string? clientToken, CancellationToken ct)
    {
        // Token se valida en API (middleware/filtro)
        if (!TryExtractKeys(payload, out var filialCode, out var docEntry, out var tipoDte, out var validationError))
            return new IngestResult(400, new { error = validationError });

        //Obtenemos el nombre de la cola
        var queueName = ResolveQueueName(tipoDte);
        //Obtenemos el tipo de mensaje para registrar en la BD
        var messageType = MessageTypeFromTipoDte(tipoDte);
        //Verificamos si el documento asociado al json ya fuen procesado
        var existing = await _sapDocs.FindByKeyAsync(filialCode, docEntry, tipoDte, ct);
        //Validacion de no existir/caso contrario, guardamos
        if (existing is not null)
        {
            if (existing.Status == SapDocumentStatus.Processed)
            {
                return new IngestResult(200, new
                {
                    message = "Documento ya procesado",
                    filialCode,
                    docEntry,
                    tipoDte,
                    queueName = existing.QueueName
                });
            }

            if (existing.Status is SapDocumentStatus.Pending or SapDocumentStatus.Processing)
            {
                return new IngestResult(202, new
                {
                    message = "Documento ya registrado y en proceso",
                    filialCode,
                    docEntry,
                    tipoDte,
                    status = existing.Status,
                    queueName = existing.QueueName
                });
            }

            if (existing.Status == SapDocumentStatus.Failed)
            {
                var hasPendingOutbox = await _outbox.HasPendingForSapDocumentAsync(existing.Id, ct);
                if (hasPendingOutbox)
                {
                    return new IngestResult(202, new
                    {
                        message = "Ya existe un outbox pendiente para este documento",
                        filialCode,
                        docEntry,
                        tipoDte,
                        queueName
                    });
                }

                await _uow.BeginTransactionAsync(ct);
                try
                {
                    var outboxBody = JsonSerializer.Serialize(new { filialCode, docEntry, tipoDte });

                    _outbox.Add(new OutboxMessage
                    {
                        Id = Guid.NewGuid(),
                        SapDocumentId = existing.Id,
                        MessageType = MessageTypeFromTipoDte(tipoDte),
                        Body = outboxBody,
                        Status = OutboxStatus.Pending,
                        PublishAttempts = 0
                    });

                    // ✅ usando behavior de dominio
                    existing.MarkPending(queueName);

                    await _uow.SaveChangesAsync(ct);
                    await _uow.CommitAsync(ct);

                    return new IngestResult(202, new
                    {
                        message = "Documento Failed re-encolado para reproceso",
                        filialCode,
                        docEntry,
                        tipoDte,
                        queueName
                    });
                }
                catch (DbUpdateException ex)
                {
                    await _uow.RollbackAsync(ct);
                    return new IngestResult(200, new
                    {
                        message = "Documento ya registrado (colisión por concurrencia)",
                        detail = ex.Message
                    });
                }
            }

            return new IngestResult(202, new
            {
                message = "Documento ya registrado",
                filialCode,
                docEntry,
                tipoDte,
                status = existing.Status,
                queueName = existing.QueueName
            });
        }

        // Nuevo
        var payloadJson = payload.GetRawText();

        await _uow.BeginTransactionAsync(ct);
        try
        {
            var sapDoc = new SapDocument
            {
                Id = Guid.NewGuid(),
                FilialCode = filialCode,
                DocEntry = docEntry,
                TipoDte = tipoDte,
                QueueName = queueName,
                PayloadJson = payloadJson,
                Status = SapDocumentStatus.Pending,
                AttemptCount = 0
            };

            _sapDocs.Add(sapDoc);

            var outboxBody = JsonSerializer.Serialize(new { filialCode, docEntry, tipoDte });

            _outbox.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                SapDocumentId = sapDoc.Id,
                MessageType = messageType,
                Body = outboxBody,
                Status = OutboxStatus.Pending,
                PublishAttempts = 0
            });

            await _uow.SaveChangesAsync(ct);
            await _uow.CommitAsync(ct);

            return new IngestResult(202, new
            {
                message = "Registrado y pendiente de encolarse",
                filialCode,
                docEntry,
                tipoDte,
                queueName
            });
        }
        catch (DbUpdateException ex)
        {
            await _uow.RollbackAsync(ct);
            return new IngestResult(200, new
            {
                message = "Documento ya registrado (colisión por concurrencia)",
                detail = ex.Message
            });
        }
    }

    // =========================
    // Helpers
    // Validación del JSON ingresado por la API
    // =========================

    private static bool TryExtractKeys(JsonElement payload, out string filialCode, out long docEntry, out int tipoDte, out string error)
    {
        filialCode = "";
        docEntry = 0;
        tipoDte = 0;
        error = "";

        if (!payload.TryGetProperty("source", out var source) ||
            !source.TryGetProperty("company", out var company))
        {
            error = "Falta source.company.";
            return false;
        }

        string? filial =
            TryGetString(company, "filialCode")
            ?? TryGetString(company, "empresa")
            ?? TryGetString(company, "filial");

        if (string.IsNullOrWhiteSpace(filial))
        {
            error = "Falta source.company.filialCode (o empresa/filial) o no es string válido.";
            return false;
        }

        if (!payload.TryGetProperty("document", out var document))
        {
            error = "Falta document.";
            return false;
        }

        if (!TryGetLong(document, "docEntry", out docEntry))
        {
            error = "Falta document.docEntry o no es numérico.";
            return false;
        }

        if (!TryGetInt(document, "tipoDte", out tipoDte))
        {
            error = "Falta document.tipoDte o no es numérico.";
            return false;
        }

        if (!payload.TryGetProperty("detalle", out var detalle) || detalle.ValueKind != JsonValueKind.Array)
        {
            error = "Falta detalle o no es un arreglo.";
            return false;
        }

        filialCode = filial!;
        return true;
    }

    private static string? TryGetString(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind != JsonValueKind.String) return null;
        var s = v.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static bool TryGetLong(JsonElement obj, string prop, out long value)
    {
        value = 0;
        if (!obj.TryGetProperty(prop, out var v)) return false;

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out value)) return true;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out value)) return true;

        return false;
    }

    private static bool TryGetInt(JsonElement obj, string prop, out int value)
    {
        value = 0;
        if (!obj.TryGetProperty(prop, out var v)) return false;

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out value)) return true;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out value)) return true;

        return false;
    }       
}