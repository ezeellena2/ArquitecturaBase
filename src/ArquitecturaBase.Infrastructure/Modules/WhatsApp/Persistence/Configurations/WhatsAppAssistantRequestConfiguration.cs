using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;

internal sealed class WhatsAppAssistantRequestConfiguration : IEntityTypeConfiguration<WhatsAppAssistantRequest>
{
    public void Configure(EntityTypeBuilder<WhatsAppAssistantRequest> builder)
    {
        builder.ToTable("WhatsAppAssistantRequests",table=>table.HasCheckConstraint("CK_WhatsAppAssistantRequests_ActorContext",
            "(\"ActorUserId\" IS NOT NULL AND \"ContextJson\" IS NOT NULL) OR (\"IsAlways\" AND \"Kind\" = 'Manual')"));
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(request => request.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(request => request.ContextJson).HasColumnType("jsonb");
        builder.Property(request => request.CapturedCulture).HasMaxLength(5);
        builder.Property(request => request.AcceptanceFingerprint).HasMaxLength(64);
        builder.HasIndex(request => request.RequestId).IsUnique();
        builder.HasIndex(request => new {request.ContactId,request.SourceRecordId}).IsUnique().HasFilter("\"SourceRecordId\" IS NOT NULL");
        builder.HasIndex(request => new { request.ContactId, request.SourceMessageId, request.Kind }).IsUnique();
        builder.HasIndex(request => new { request.ContactId, request.State, request.CreatedAtUtc, request.Id });
        builder.HasOne<WhatsAppContact>().WithMany().HasForeignKey(request => request.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WhatsAppMessage>().WithMany().HasForeignKey(request => request.SourceMessageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WhatsAppOutgoingMessage>().WithMany().HasForeignKey(request => request.OutgoingId).OnDelete(DeleteBehavior.Restrict);
    }
}
