using HUBDTE.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HUBDTE.Infrastructure.Persistence
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<SapDocument> SapDocuments => Set<SapDocument>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
            base.OnModelCreating(modelBuilder);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var utcNow = DateTime.UtcNow;

            ApplySapDocumentAudit(utcNow);
            ApplyOutboxMessageAudit(utcNow);

            return base.SaveChangesAsync(cancellationToken);
        }

        private void ApplySapDocumentAudit(DateTime utcNow)
        {
            foreach (var entry in ChangeTracker.Entries<SapDocument>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.Id = entry.Entity.Id == Guid.Empty ? Guid.NewGuid() : entry.Entity.Id;
                    entry.Entity.CreatedAt = utcNow;
                    entry.Entity.UpdatedAt = utcNow;
                    continue;
                }

                if (entry.State == EntityState.Modified)
                {
                    entry.Entity.UpdatedAt = utcNow;
                }
            }
        }

        private void ApplyOutboxMessageAudit(DateTime utcNow)
        {
            foreach (var entry in ChangeTracker.Entries<OutboxMessage>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.Id = entry.Entity.Id == Guid.Empty ? Guid.NewGuid() : entry.Entity.Id;
                    entry.Entity.CreatedAt = utcNow;
                }
            }
        }
    }
}