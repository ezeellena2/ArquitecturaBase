using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;
internal sealed class WhatsAppFlowSessionConfiguration : IEntityTypeConfiguration<WhatsAppFlowSession>
{
    public void Configure(EntityTypeBuilder<WhatsAppFlowSession> builder)
    {
        builder.ToTable("WhatsAppFlowSessions"); builder.HasKey(session => session.Id);
        builder.HasAlternateKey(session => new { session.Id, session.ContactId, session.OperatorUserId });
        builder.Property(session => session.Culture).HasMaxLength(2);
        builder.Property(session => session.NameJson).HasColumnType("jsonb");
        builder.Property(session => session.TokenHash).HasMaxLength(64);
        builder.Property(session => session.RequestFingerprint).HasMaxLength(64);
        builder.Property(session => session.ProtectedPayload).HasColumnType("bytea");
        builder.HasIndex(session => session.TokenHash).IsUnique();
        builder.HasIndex(session => new { session.ExpiresAtUtc, session.Id });
        builder.HasOne<WhatsAppContact>().WithMany().HasForeignKey(session => session.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WhatsAppFlowBinding>().WithMany().HasForeignKey(session => new { session.BindingId, session.VersionId })
            .HasPrincipalKey(binding => new { binding.Id, binding.VersionId }).OnDelete(DeleteBehavior.Restrict);
    }
}
