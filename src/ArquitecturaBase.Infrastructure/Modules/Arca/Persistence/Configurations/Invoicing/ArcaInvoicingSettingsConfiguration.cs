using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Configurations.Invoicing;

internal sealed class ArcaInvoicingSettingsConfiguration : IEntityTypeConfiguration<ArcaInvoicingSettings>
{
    public void Configure(EntityTypeBuilder<ArcaInvoicingSettings> builder)
    {
        builder.ToTable("ArcaInvoicingSettings");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ConnectionId).IsUnique();
        builder.HasOne<ArcaConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.GrossIncomeNumber).HasMaxLength(30);
    }
}
