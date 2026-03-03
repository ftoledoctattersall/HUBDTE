using HUBDTE.Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace HUBDTE.Infrastructure.Persistence
{
    public class EfUnitOfWork : IUnitOfWork, IAsyncDisposable
    {
        private readonly AppDbContext _db;
        private IDbContextTransaction? _tx;

        public EfUnitOfWork(AppDbContext db)
        {
            _db = db;
        }

        public async Task BeginTransactionAsync(CancellationToken ct)
        {
            if (_tx is not null)
                return;

            _tx = await _db.Database.BeginTransactionAsync(ct);
        }

        public async Task CommitAsync(CancellationToken ct)
        {
            if (_tx is null)
                return;

            await _tx.CommitAsync(ct);
            await DisposeTransactionAsync();
        }

        public async Task RollbackAsync(CancellationToken ct)
        {
            if (_tx is null)
                return;

            await _tx.RollbackAsync(ct);
            await DisposeTransactionAsync();
        }

        public Task<int> SaveChangesAsync(CancellationToken ct)
            => _db.SaveChangesAsync(ct);

        public async ValueTask DisposeAsync()
        {
            await DisposeTransactionAsync();
        }

        private async Task DisposeTransactionAsync()
        {
            if (_tx is null)
                return;

            await _tx.DisposeAsync();
            _tx = null;
        }
    }
}