using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Configurations.Invoicing;

/// <summary>
/// La cola de emisión (ADR 0028). Sin clave foránea a la conexión: desconectar no borra comprobantes, que tienen valor legal.
/// Un número no se repite en su secuencia mientras lo tenga un comprobante; la clave del pedido es única.
/// </summary>
internal sealed class ArcaVoucherConfiguration : IEntityTypeConfiguration<ArcaVoucher>
{
    public void Configure(EntityTypeBuilder<ArcaVoucher> builder)
    {
        builder.ToTable("ArcaVouchers");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.RequestKey).IsUnique();
        builder.HasIndex(x => new { x.Environment, x.Cuit, x.PointOfSale, x.VoucherType, x.Number }).IsUnique().HasFilter("\"Number\" IS NOT NULL");
        builder.HasIndex(x => new { x.State, x.NextAttemptAtUtc });
        builder.HasIndex(x => x.CancelsVoucherId);
        // H27: one live credit note per invoice is a rule of the base too, not only of the lock.
        builder.HasIndex(x => x.CancelsVoucherId, "IX_ArcaVouchers_CancelsVoucherId_Live").IsUnique()
            .HasFilter("\"CancelsVoucherId\" IS NOT NULL AND \"State\" NOT IN ('Rejected', 'Cancelled')");
        // H34: the PDFs that are missing, without going through every authorized voucher.
        builder.HasIndex(x => x.NextAttemptAtUtc, "IX_ArcaVouchers_PdfDue").HasFilter("\"State\" = 'Authorized' AND \"PdfStorageKey\" IS NULL");
        builder.HasOne<ArcaVoucher>().WithMany().HasForeignKey(x => x.CancelsVoucherId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Environment).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Class).HasConversion<string>().HasMaxLength(2);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ReviewReason).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Cuit).HasMaxLength(11);
        builder.Property(x => x.Total).HasPrecision(15, 2);
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb");
        builder.Property(x => x.MessagesJson).HasColumnType("jsonb");
        builder.Property(x => x.DataHash).HasMaxLength(64);
        builder.Property(x => x.Cae).HasMaxLength(14);
        builder.Property(x => x.FailureCode).HasMaxLength(120);
        builder.Property(x => x.PdfStorageKey).HasMaxLength(200);
        builder.Property(x => x.PdfSha256).HasMaxLength(64);
        builder.Property(x => x.Revision).IsConcurrencyToken();
    }
}
