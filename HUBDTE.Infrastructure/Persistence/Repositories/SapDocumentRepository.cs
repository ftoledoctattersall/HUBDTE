using HUBDTE.Application.Interfaces.Persistence;
using HUBDTE.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HUBDTE.Infrastructure.Persistence.Repositories
{
    public class SapDocumentRepository : ISapDocumentRepository
    {
        private readonly AppDbContext _db;

        public SapDocumentRepository(AppDbContext db)
        {
            _db = db;
        }

        public Task<SapDocument?> GetByIdAsync(Guid id, CancellationToken ct)
        {
            return _db.SapDocuments.FirstOrDefaultAsync(x => x.Id == id, ct);
        }

        public Task<SapDocument?> FindByKeyAsync(string filialCode, long docEntry, int tipoDte, CancellationToken ct)
        {
            return _db.SapDocuments.FirstOrDefaultAsync(
                x => x.FilialCode == filialCode && x.DocEntry == docEntry && x.TipoDte == tipoDte,
                ct);
        }

        public async Task<bool> TryClaimForProcessingAsync(string filialCode, long docEntry, int tipoDte, CancellationToken ct)
        {
            var sql = @"
UPDATE dbo.SapDocuments
SET Status = {0},
    UpdatedAt = SYSUTCDATETIME()
WHERE FilialCode = {1}
  AND DocEntry = {2}
  AND TipoDte = {3}
  AND Status <> {4}
  AND Status <> {5};
";

            var rows = await _db.Database.ExecuteSqlRawAsync(
                sql,
                new object[]
                {
                    (byte)SapDocumentStatus.Processing,
                    filialCode,
                    docEntry,
                    tipoDte,
                    (byte)SapDocumentStatus.Processed,
                    (byte)SapDocumentStatus.Processing
                },
                ct);

            return rows > 0;
        }

        public void Add(SapDocument doc) => _db.SapDocuments.Add(doc);
    }
}