using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormRecordCatalogFieldConfiguration : IEntityTypeConfiguration<BusinessFormRecordCatalogField>
{
    public void Configure(EntityTypeBuilder<BusinessFormRecordCatalogField> builder)
    {
        builder.ToTable("BusinessFormRecordCatalogFields");
        builder.HasKey(value => value.Id);
        builder.HasIndex(value => new { value.DefinitionId, value.CatalogId, value.FieldId }).IsUnique();
        builder.Property(value => value.Key).HasMaxLength(40);
        builder.Property(value => value.NameJson).HasColumnType("jsonb");
        builder.Property(value => value.Type).HasMaxLength(16);
        builder.Property(value => value.Role).HasMaxLength(16);
        builder.HasOne<BusinessFormDefinition>().WithMany().HasForeignKey(value => value.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessFormCatalog>().WithMany().HasForeignKey(value => value.CatalogId).OnDelete(DeleteBehavior.Restrict);
    }
}
