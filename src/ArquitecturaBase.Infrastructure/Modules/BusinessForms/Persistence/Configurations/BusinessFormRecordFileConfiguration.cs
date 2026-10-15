using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormRecordFileConfiguration : IEntityTypeConfiguration<BusinessFormRecordFile>
{
    public void Configure(EntityTypeBuilder<BusinessFormRecordFile> builder)
    {
        builder.ToTable("BusinessFormRecordFiles", table => table.HasCheckConstraint("CK_BusinessFormRecordFiles_Position", "\"Position\" >= 0 AND \"Position\" < 10"));
        builder.HasKey(file => file.Id);
        builder.HasIndex(file => new { file.RecordId, file.FieldId, file.Position }).IsUnique();
        builder.HasIndex(file => file.FileId).IsUnique();
        builder.HasOne<BusinessFormRecord>().WithMany().HasForeignKey(file => new { file.DefinitionId, file.RecordId })
            .HasPrincipalKey(record => new { record.DefinitionId, record.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<BusinessFormField>().WithMany().HasForeignKey(file => new { file.DefinitionId, file.FieldId })
            .HasPrincipalKey(field => new { field.DefinitionId, field.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(file => file.FileId).OnDelete(DeleteBehavior.Restrict);
    }
}
