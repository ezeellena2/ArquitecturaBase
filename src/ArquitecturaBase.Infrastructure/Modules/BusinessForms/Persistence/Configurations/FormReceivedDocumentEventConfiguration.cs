using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

// The history of a received document: it goes with its document.
internal sealed class FormReceivedDocumentEventConfiguration : IEntityTypeConfiguration<FormReceivedDocumentEvent>
{
    public void Configure(EntityTypeBuilder<FormReceivedDocumentEvent> builder)
    {
        builder.ToTable("FormReceivedDocumentEvents");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Kind).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(value => new { value.DocumentId, value.AtUtc });
        builder.HasOne<FormReceivedDocument>().WithMany().HasForeignKey(value => value.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}
