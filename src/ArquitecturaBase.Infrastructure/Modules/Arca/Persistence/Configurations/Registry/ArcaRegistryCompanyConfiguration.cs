using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Registry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Configurations.Registry;

internal sealed class ArcaRegistryCompanyConfiguration : IEntityTypeConfiguration<ArcaRegistryCompany>
{
    public void Configure(EntityTypeBuilder<ArcaRegistryCompany> builder)
    {
        builder.ToTable("ArcaRegistryCompanies");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ConnectionId).IsUnique();
        builder.HasOne<ArcaConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.LegalName).HasMaxLength(300);
        builder.Property(x => x.VatCondition).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.FiscalAddress).HasMaxLength(500);
    }
}
