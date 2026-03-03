using HUBDTE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HUBDTE.Infrastructure.Persistence.Configurations
{
    public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
    {
        public void Configure(EntityTypeBuilder<OutboxMessage> builder)
        {
            builder.ToTable("OutboxMessages", "dbo");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.MessageType).HasMaxLength(50).IsRequired();
            builder.Property(x => x.Body).IsRequired();

            builder.Property(x => x.Status).HasConversion<byte>().IsRequired();
            builder.Property(x => x.PublishAttempts).HasDefaultValue(0);

            builder.Property(x => x.ProcessingStartedAt).HasColumnType("datetime2");
            builder.Property(x => x.PublishedAt).HasColumnType("datetime2");

            builder.Property(x => x.LockId).HasColumnType("uniqueidentifier");
            builder.Property(x => x.LockedAt).HasColumnType("datetime2");

            builder.Property(x => x.Error).HasMaxLength(2000);

            builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(x => x.CorrelationId).HasMaxLength(100);
            builder.Property(x => x.MessageTypeHeader).HasMaxLength(50);

            builder.HasIndex(x => x.CorrelationId)
                   .HasDatabaseName("IX_OutboxMessages_CorrelationId");

            builder.HasOne(x => x.SapDocument)
                   .WithMany(d => d.OutboxMessages)
                   .HasForeignKey(x => x.SapDocumentId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => new { x.Status, x.CreatedAt })
                   .HasDatabaseName("IX_OutboxMessages_Status_CreatedAt");

            builder.HasIndex(x => new { x.Status, x.ProcessingStartedAt })
                   .HasDatabaseName("IX_OutboxMessages_Status_ProcessingStartedAt");

            builder.HasIndex(x => new { x.Status, x.LockId, x.CreatedAt })
                   .HasDatabaseName("IX_OutboxMessages_Status_LockId_CreatedAt");

            builder.HasIndex(x => new { x.Status, x.LockedAt })
                   .HasDatabaseName("IX_OutboxMessages_Status_LockedAt");
        }
    }
}