using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormPersonAttributeConfiguration : IEntityTypeConfiguration<BusinessFormPersonAttribute>
{
    public void Configure(EntityTypeBuilder<BusinessFormPersonAttribute> builder)
    {
        builder.ToTable("BusinessFormPersonAttributes", table => table.HasCheckConstraint(
            "CK_BusinessFormPersonAttributes_Source", "(\"Type\" = 'Catalog') = (\"SourceCatalogId\" IS NOT NULL)"));
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Key).HasMaxLength(40);
        builder.HasIndex(value => value.Key).IsUnique();
        builder.Property(value => value.NameJson).HasColumnType("jsonb");
        builder.Property(value => value.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.Revision).IsConcurrencyToken();
        builder.HasOne<BusinessFormCatalog>().WithMany().HasForeignKey(value => value.SourceCatalogId).OnDelete(DeleteBehavior.Restrict);
    }
}
