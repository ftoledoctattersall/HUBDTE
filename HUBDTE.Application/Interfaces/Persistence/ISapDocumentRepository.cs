using HUBDTE.Domain.Entities;

namespace HUBDTE.Application.Interfaces.Persistence;

public interface ISapDocumentRepository
{
    Task<SapDocument?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<SapDocument?> FindByKeyAsync(string filialCode, long docEntry, int tipoDte, CancellationToken ct);
    Task<bool> TryClaimForProcessingAsync(string filialCode, long docEntry, int tipoDte, CancellationToken ct);
    void Add(SapDocument doc);
}