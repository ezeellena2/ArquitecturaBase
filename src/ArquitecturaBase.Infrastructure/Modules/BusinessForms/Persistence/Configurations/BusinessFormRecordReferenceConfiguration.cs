using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormRecordReferenceConfiguration : IEntityTypeConfiguration<BusinessFormRecordReference>
{
    public void Configure(EntityTypeBuilder<BusinessFormRecordReference> builder)
    {
        builder.ToTable("BusinessFormRecordReferences", table =>
        {
            table.HasCheckConstraint("CK_BusinessFormRecordReferences_Value", "(\"Value\" IS NULL) = (\"ValueType\" IS NULL)");
            table.HasCheckConstraint("CK_BusinessFormRecordReferences_Row", "\"Row\" >= 0 AND \"Row\" < 100");
        });
        builder.HasKey(value => value.Id);
        // The same option can be chosen in two items of a block, never twice in one.
        builder.HasIndex(value => new { value.RecordId, value.FieldId, value.Row, value.ItemId }).IsUnique();
        builder.Property(value => value.Row).HasDefaultValue(0);
        builder.Property(value => value.Text).HasMaxLength(30);
        builder.Property(value => value.Detail).HasMaxLength(300);
        builder.Property(value => value.CatalogName).HasMaxLength(200);
        builder.Property(value => value.TranslationsJson).HasColumnType("jsonb");
        builder.Property(value => value.FieldValuesJson).HasColumnType("jsonb");
        builder.Property(value => value.Value).HasMaxLength(64);
        builder.Property(value => value.ValueType).HasMaxLength(16);
        builder.HasOne<BusinessFormRecord>().WithMany().HasForeignKey(value => new { value.DefinitionId, value.RecordId })
            .HasPrincipalKey(value => new { value.DefinitionId, value.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<BusinessFormField>().WithMany().HasForeignKey(value => new { value.DefinitionId, value.FieldId })
            .HasPrincipalKey(value => new { value.DefinitionId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessFormCatalog>().WithMany().HasForeignKey(value => value.CatalogId).OnDelete(DeleteBehavior.Restrict);
        // Filters item ids refer to the versioned source view, not to manual items.
    }
}
