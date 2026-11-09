using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Configurations.Invoicing;

/// <summary>El candado de numeración: una fila por ambiente, CUIT, punto de venta y tipo, sin clave foránea a la conexión.</summary>
internal sealed class ArcaVoucherSequenceConfiguration : IEntityTypeConfiguration<ArcaVoucherSequence>
{
    public void Configure(EntityTypeBuilder<ArcaVoucherSequence> builder)
    {
        builder.ToTable("ArcaVoucherSequences");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Environment, x.Cuit, x.PointOfSale, x.VoucherType }).IsUnique();
        builder.Property(x => x.Environment).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Cuit).HasMaxLength(11);
        builder.Property(x => x.Fence).IsConcurrencyToken();
    }
}
