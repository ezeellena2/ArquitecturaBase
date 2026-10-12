using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormCatalogUserViewConfiguration : IEntityTypeConfiguration<BusinessFormCatalogUserView>
{
    public void Configure(EntityTypeBuilder<BusinessFormCatalogUserView> builder)
    {
        builder.HasNoKey();
        builder.ToView("BusinessFormCatalogUsers");
        builder.Property(value => value.Id).HasColumnName("Id");
        builder.Property(value => value.Name).HasColumnName("Name");
        builder.Property(value => value.Roles).HasColumnName("Roles");
        builder.Property(value => value.Active).HasColumnName("Active");
    }
}
