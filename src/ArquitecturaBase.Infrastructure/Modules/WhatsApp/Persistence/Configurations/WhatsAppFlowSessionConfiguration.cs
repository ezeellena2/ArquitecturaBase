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
        // The previous binary omits these columns during the bundle-to-image deployment interval.
        builder.Property(session=>session.Origin).HasConversion<string>().HasMaxLength(24).HasDefaultValue(WhatsAppMessageOrigin.Conversation);
        builder.Property(session=>session.ProtectedAssistantContent).HasColumnType("bytea").HasDefaultValue(Array.Empty<byte>());
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
        // W1-A: the location asked before the Flow. A send of before has none.
        builder.Property(session => session.LocationState).HasConversion<string>().HasMaxLength(16).HasDefaultValue(WhatsAppFlowLocationState.None);
        builder.Property(session => session.LocationLatitude).HasPrecision(9, 6);
        builder.Property(session => session.LocationLongitude).HasPrecision(9, 6);
        builder.Property(session => session.LocationWaMessageId).HasMaxLength(WhatsAppMessage.MaxWaMessageIdLength);
        builder.Property(session => session.FlowBody).HasMaxLength(1024);
        // W3: what the send took of what the person shared (task 23); a send of before took nothing.
        builder.Property(session => session.SharedContentJson).HasColumnType("jsonb");
        builder.Property(session => session.ProtectedSharedValues).HasColumnType("bytea");
        builder.HasIndex(session => new { session.ContactId, session.ExpiresAtUtc })
            .HasFilter("\"LocationState\" = 'Requested' AND \"CaptureState\" = 'Pending'");
        // The filtered index above does not cover every send: the foreign key keeps its own.
        builder.HasIndex(session => session.ContactId);
        builder.HasIndex(session => session.ResponseWaMessageId).IsUnique().HasFilter("\"ResponseWaMessageId\" IS NOT NULL");
        builder.HasIndex(session => session.TokenHash).IsUnique();
        builder.HasIndex(session => new { session.ExpiresAtUtc, session.Id });
        builder.HasIndex(session => new { session.VersionId, session.ExpiresAtUtc }).HasFilter("\"CaptureState\" = 'Pending'");
        builder.HasOne<WhatsAppContact>().WithMany().HasForeignKey(session => session.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WhatsAppFlowBinding>().WithMany().HasForeignKey(session => new { session.BindingId, session.VersionId })
            .HasPrincipalKey(binding => new { binding.Id, binding.VersionId }).OnDelete(DeleteBehavior.Restrict);
    }
}
