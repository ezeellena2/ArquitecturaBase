using ArquitecturaBase.Domain.Modules.Arca.Connection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Configurations.Connection;

internal sealed class ArcaAccessTicketConfiguration : IEntityTypeConfiguration<ArcaAccessTicket>
{
    public void Configure(EntityTypeBuilder<ArcaAccessTicket> builder)
    {
        builder.ToTable("ArcaAccessTickets");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CertificateId, x.ServiceCode }).IsUnique();
        builder.HasOne<ArcaCertificate>().WithMany().HasForeignKey(x => x.CertificateId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.ServiceCode).HasMaxLength(40);
        builder.Property(x => x.ProtectedCredentials).HasMaxLength(16000);
    }
}
