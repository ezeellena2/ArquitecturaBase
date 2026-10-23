using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormPersonDataConfiguration : IEntityTypeConfiguration<BusinessFormPersonData>
{
    public void Configure(EntityTypeBuilder<BusinessFormPersonData> builder)
    {
        builder.ToTable("BusinessFormPersonData");
        builder.HasKey(value => value.Id);
        builder.HasIndex(value => value.UserId).IsUnique();
        builder.Property(value => value.ValuesJson).HasColumnType("jsonb").HasDefaultValue("{}");
        builder.Property(value => value.Revision).IsConcurrencyToken();
    }
}
