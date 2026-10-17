using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;
internal sealed class WhatsAppFlowSessionConfiguration : IEntityTypeConfiguration<WhatsAppFlowSession>
{
    public void Configure(EntityTypeBuilder<WhatsAppFlowSession> builder)
    {
        builder.ToTable("WhatsAppFlowSessions"); builder.HasKey(session => session.Id);
        builder.ToTable(table=>table.HasCheckConstraint("CK_WhatsAppFlowSession_OperatorOrigin", "(\"Origin\" = 'Assistant' AND \"OperatorUserId\" IS NULL) OR (\"Origin\" = 'Conversation' AND \"OperatorUserId\" IS NOT NULL)"));
        builder.Property(session=>session.Origin).HasConversion<string>().HasMaxLength(24);
        builder.Property(session=>session.ProtectedAssistantContent).HasColumnType("bytea");
        builder.Property(session => session.Culture).HasMaxLength(2);
        builder.Property(session => session.NameJson).HasColumnType("jsonb");
        builder.Property(session => session.Prefix).HasMaxLength(12);
        builder.Property(session => session.TokenHash).HasMaxLength(64);
        builder.Property(session => session.RequestFingerprint).HasMaxLength(64);
        builder.Property(session => session.ProtectedPayload).HasColumnType("bytea");
        builder.Property(session => session.CaptureState).HasConversion<string>().HasMaxLength(16);
        builder.Property(session => session.CaptureErrorCode).HasMaxLength(128);
        builder.Property(session => session.RejectedQuestion).HasMaxLength(80);
        builder.Property(session => session.ResponseWaMessageId).HasMaxLength(WhatsAppMessage.MaxWaMessageIdLength);
        builder.HasIndex(session => session.ResponseWaMessageId).IsUnique().HasFilter("\"ResponseWaMessageId\" IS NOT NULL");
        builder.HasIndex(session => session.TokenHash).IsUnique();
        builder.HasIndex(session => new { session.ExpiresAtUtc, session.Id });
        builder.HasOne<WhatsAppContact>().WithMany().HasForeignKey(session => session.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WhatsAppFlowBinding>().WithMany().HasForeignKey(session => new { session.BindingId, session.VersionId })
            .HasPrincipalKey(binding => new { binding.Id, binding.VersionId }).OnDelete(DeleteBehavior.Restrict);
    }
}
