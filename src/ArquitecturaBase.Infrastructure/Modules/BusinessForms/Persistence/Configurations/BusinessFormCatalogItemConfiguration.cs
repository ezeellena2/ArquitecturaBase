using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormCatalogItemConfiguration : IEntityTypeConfiguration<BusinessFormCatalogItem>
{
    public void Configure(EntityTypeBuilder<BusinessFormCatalogItem> builder)
    {
        builder.ToTable("BusinessFormCatalogItems");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Code).HasMaxLength(80);
        builder.Property(value => value.TextEs).HasMaxLength(200);
        builder.Property(value => value.TextEn).HasMaxLength(200);
        builder.Property(value => value.DetailEs).HasMaxLength(500);
        builder.Property(value => value.DetailEn).HasMaxLength(500);
        builder.HasIndex(value => new { value.CatalogId, value.IsActive, value.Position, value.Id });
        builder.HasOne<BusinessFormCatalog>().WithMany().HasForeignKey(value => value.CatalogId).OnDelete(DeleteBehavior.Restrict);
    }
}
