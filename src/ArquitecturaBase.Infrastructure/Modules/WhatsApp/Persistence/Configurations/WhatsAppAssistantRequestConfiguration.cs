using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;

internal sealed class WhatsAppAssistantRequestConfiguration : IEntityTypeConfiguration<WhatsAppAssistantRequest>
{
    public void Configure(EntityTypeBuilder<WhatsAppAssistantRequest> builder)
    {
        // IA, Stage 2: what follows a captured response never carries a spreadsheet.
        builder.ToTable("WhatsAppAssistantRequests",table=>
        {
            table.HasCheckConstraint("CK_WhatsAppAssistantRequests_ActorContext",
                "(\"ActorUserId\" IS NOT NULL AND \"ContextJson\" IS NOT NULL) OR (\"IsAlways\" AND \"Kind\" = 'Manual')");
            table.HasCheckConstraint("CK_WhatsAppAssistantRequests_Attachment","\"AttachmentMessageId\" IS NULL OR \"SourceRecordId\" IS NULL");
        });
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(request => request.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(request => request.ContextJson).HasColumnType("jsonb");
        builder.Property(request => request.CapturedCulture).HasMaxLength(5);
        builder.Property(request => request.AcceptanceFingerprint).HasMaxLength(64);
        builder.HasIndex(request => request.RequestId).IsUnique();
        builder.HasIndex(request => new {request.ContactId,request.SourceRecordId}).IsUnique().HasFilter("\"SourceRecordId\" IS NOT NULL");
        // Casos del asistente: the second and third actions of a reply have the source of the first one and their own ordinal.
        builder.HasIndex(request => new { request.ContactId, request.SourceMessageId, request.Kind, request.ActionOrdinal }).IsUnique();
        builder.Property(request => request.OutcomeJson).HasColumnType("jsonb");
        builder.HasIndex(request => new { request.ContactId, request.State, request.CreatedAtUtc, request.Id });
        builder.HasOne<WhatsAppContact>().WithMany().HasForeignKey(request => request.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WhatsAppMessage>().WithMany().HasForeignKey(request => request.SourceMessageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WhatsAppOutgoingMessage>().WithMany().HasForeignKey(request => request.OutgoingId).OnDelete(DeleteBehavior.Restrict);
    }
}
