using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormCatalogConfiguration : IEntityTypeConfiguration<BusinessFormCatalog>
{
    public void Configure(EntityTypeBuilder<BusinessFormCatalog> builder)
    {
        builder.ToTable("BusinessFormCatalogs");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Key).HasMaxLength(32);
        builder.HasIndex(value => value.Key).IsUnique();
        builder.Property(value => value.NameJson).HasColumnType("jsonb");
        builder.Property(value => value.DescriptionJson).HasColumnType("jsonb");
        builder.Property<string>("DisplayNameEs").HasMaxLength(120)
            .HasComputedColumnSql("COALESCE(NULLIF(\"NameJson\" ->> 'es', ''), '')", stored: true);
        builder.Property<string>("DisplayNameEn").HasMaxLength(120)
            .HasComputedColumnSql("COALESCE(NULLIF(\"NameJson\" ->> 'en', ''), '')", stored: true);
        builder.Property<string>("DisplayDescriptionEs").HasMaxLength(1000)
            .HasComputedColumnSql("COALESCE(NULLIF(\"DescriptionJson\" ->> 'es', ''), '')", stored: true);
        builder.Property<string>("DisplayDescriptionEn").HasMaxLength(1000)
            .HasComputedColumnSql("COALESCE(NULLIF(\"DescriptionJson\" ->> 'en', ''), '')", stored: true);
        builder.Property(value => value.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.Revision).IsConcurrencyToken();
    }
}
