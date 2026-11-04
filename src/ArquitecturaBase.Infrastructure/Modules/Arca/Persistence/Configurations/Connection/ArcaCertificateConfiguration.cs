using ArquitecturaBase.Domain.Modules.Arca.Connection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Configurations.Connection;

internal sealed class ArcaCertificateConfiguration : IEntityTypeConfiguration<ArcaCertificate>
{
    public void Configure(EntityTypeBuilder<ArcaCertificate> builder)
    {
        builder.ToTable("ArcaCertificates");
        builder.HasKey(x => x.Id);
        // Uno activo y, durante una renovación, uno pendiente.
        builder.HasIndex(x => new { x.ConnectionId, x.Role }).IsUnique();
        builder.HasOne<ArcaConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Alias).HasMaxLength(30);
        builder.Property(x => x.RequestPem).HasMaxLength(8000);
        builder.Property(x => x.ProtectedPrivateKey).HasMaxLength(8000);
        builder.Property(x => x.CertificatePem).HasMaxLength(16000);
        builder.Property(x => x.Thumbprint).HasMaxLength(128);
    }
}
