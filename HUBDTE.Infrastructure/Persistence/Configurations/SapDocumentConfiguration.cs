using HUBDTE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;


namespace HUBDTE.Infrastructure.Persistence.Configurations
{
    public class SapDocumentConfiguration : IEntityTypeConfiguration<SapDocument>
    {
        public void Configure(EntityTypeBuilder<SapDocument> builder)
        {
            builder.ToTable("SapDocuments", "dbo");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.FilialCode).HasMaxLength(50).IsRequired();
            builder.Property(x => x.DocEntry).IsRequired();
            builder.Property(x => x.TipoDte).IsRequired();

            builder.Property(x => x.QueueName).HasMaxLength(100).IsRequired();
            builder.Property(x => x.PayloadJson).IsRequired();

            builder.Property(x => x.Status).HasConversion<byte>();
            builder.Property(x => x.AttemptCount).HasDefaultValue(0);

            builder.Property(x => x.ErrorReason);

            builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            builder.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            builder.HasIndex(x => new { x.FilialCode, x.DocEntry, x.TipoDte })
                   .IsUnique()
                   .HasDatabaseName("UX_SapDocuments_Filial_DocEntry_TipoDte");

            builder.HasIndex(x => new { x.Status, x.UpdatedAt })
                   .HasDatabaseName("IX_SapDocuments_Status_UpdatedAt");
        }
    }
}
