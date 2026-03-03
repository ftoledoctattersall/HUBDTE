namespace HUBDTE.Application.Interfaces.Persistence;

public interface IUnitOfWork
{
    Task BeginTransactionAsync(CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}