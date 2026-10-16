using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;
internal sealed class BusinessFormDocumentConfiguration : IEntityTypeConfiguration<BusinessFormDocument>
{
    public void Configure(EntityTypeBuilder<BusinessFormDocument> builder)
    {
        builder.ToTable("BusinessFormDocuments");
        builder.HasKey(document => document.Id);
        builder.HasIndex(document => new { document.RunId, document.Culture }).IsUnique();
        builder.Property(document => document.Culture).HasMaxLength(2);
        builder.HasOne<BusinessFormRecord>().WithMany().HasForeignKey(document => new { document.DefinitionId, document.RecordId })
            .HasPrincipalKey(record => new { record.DefinitionId, record.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<BusinessFormActionRun>().WithMany().HasForeignKey(document => new { document.RecordId, document.RunId })
            .HasPrincipalKey(run => new { run.RecordId, run.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessFormPdfDesign>().WithMany().HasForeignKey(document => new { document.DefinitionId, document.DesignId })
            .HasPrincipalKey(design => new { design.DefinitionId, design.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(document => document.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}
