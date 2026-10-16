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
        builder.Property(item => item.Origin).HasConversion<string>().HasMaxLength(20).HasDefaultValue(WhatsAppMessageOrigin.Conversation);
        builder.Property(item => item.RecipientWaId).HasMaxLength(32);
        builder.Property(item => item.FailureCode).HasMaxLength(128);
        builder.Property(item => item.DocumentStorageKey).HasMaxLength(512);
        builder.Property(item => item.DocumentName).HasMaxLength(255);
        builder.Property(item => item.DocumentContentType).HasMaxLength(128);
        builder.Property(item => item.DocumentSha256).HasMaxLength(64);
        builder.Property(item => item.DocumentMediaId).HasMaxLength(128);
        builder.HasIndex(item => new { item.RequestId, item.RecipientUserId }).IsUnique()
            .HasFilter("\"Origin\" = 'FormAction'");
        builder.Property(item => item.Body).HasMaxLength(WhatsAppMessage.MaxBodyLength);
        builder.Property(item => item.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(item => item.WaMessageId).HasMaxLength(WhatsAppMessage.MaxWaMessageIdLength);
        builder.HasIndex(item => new { item.ContactId, item.OperatorUserId, item.RequestId }).IsUnique();
        builder.HasIndex(item => new { item.State, item.RequestedAtUtc, item.Id });
        builder.HasOne<WhatsAppContact>().WithMany().HasForeignKey(item => item.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.FlowSession).WithMany().HasForeignKey(item => item.FlowSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
