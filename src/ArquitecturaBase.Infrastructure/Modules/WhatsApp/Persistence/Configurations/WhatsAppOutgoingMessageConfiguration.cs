using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;

internal sealed class WhatsAppOutgoingMessageConfiguration : IEntityTypeConfiguration<WhatsAppOutgoingMessage>
{
    public void Configure(EntityTypeBuilder<WhatsAppOutgoingMessage> builder)
    {
        builder.ToTable("WhatsAppOutgoingMessages");
        builder.Ignore(item => item.Document);
        builder.Ignore(item => item.QuickReplies);
        // H16 (ADR 0030): what a send says (its text, its buttons, its list, its footer, its document's name, its rows and why it
        // went) is kept encrypted.
        builder.Property(item => item.QuickRepliesJson).HasConversion(WhatsAppChatContent.Text).HasColumnType("bytea");
        builder.Property(item => item.IsAssistantHandoff).HasDefaultValue(false);
        builder.Property(item => item.AssistantReplyJson).HasConversion(WhatsAppChatContent.Text).HasColumnType("bytea");
        builder.Property(item => item.ListButton).HasConversion(WhatsAppChatContent.Text).HasColumnType("bytea");
        builder.HasIndex(item => new { item.ContactId, item.RequestId }).IsUnique().HasFilter("\"Origin\" = 'Assistant'");
        builder.Property(item => item.Origin).HasConversion<string>().HasMaxLength(20).HasDefaultValue(WhatsAppMessageOrigin.Conversation);
        builder.Property(item => item.RecipientWaId).HasMaxLength(32);
        builder.Property(item => item.FailureCode).HasMaxLength(128);
        builder.Property(item => item.DocumentStorageKey).HasMaxLength(512);
        builder.Property(item => item.DocumentName).HasConversion(WhatsAppChatContent.Text).HasColumnType("bytea");
        builder.Property(item => item.DocumentContentType).HasMaxLength(128);
        builder.Property(item => item.DocumentSha256).HasMaxLength(64);
        builder.Property(item => item.DocumentMediaId).HasMaxLength(128);
        builder.HasIndex(item => new { item.RequestId, item.RecipientUserId }).IsUnique()
            .HasFilter("\"Origin\" = 'FormAction'");
        builder.Property(item => item.Body).HasConversion(WhatsAppChatContent.Text).HasColumnType("bytea");
        builder.Property(item => item.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(item => item.WaMessageId).HasMaxLength(WhatsAppMessage.MaxWaMessageIdLength);
        builder.Property(item => item.TraceParent).HasMaxLength(WhatsAppMessage.TraceParentLength);
        // W3: a resend of the form of a send (task 23); every outgoing of before is not one.
        builder.Property(item => item.IsResend).HasDefaultValue(false);
        // H31: the mark of a rule's notice, which goes after what answers someone when the next chat is chosen.
        builder.Property(item => item.IsNotice).HasDefaultValue(false);
        // VC1-A (task 29): the footer of the Flow of a load, only for Meta, and «Por qué lo propuso» of the notice of the loads.
        builder.Property(item => item.Footer).HasConversion(WhatsAppChatContent.Text).HasColumnType("bytea");
        builder.Property(item => item.SpreadsheetRowsJson).HasConversion(WhatsAppChatContent.Text).HasColumnType("bytea");
        builder.HasIndex(item => new { item.ContactId, item.OperatorUserId, item.RequestId }).IsUnique();
        builder.HasIndex(item => new { item.State, item.IsNotice, item.RequestedAtUtc, item.Id });
        builder.HasOne<WhatsAppContact>().WithMany().HasForeignKey(item => item.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.FlowSession).WithMany().HasForeignKey(item => item.FlowSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
