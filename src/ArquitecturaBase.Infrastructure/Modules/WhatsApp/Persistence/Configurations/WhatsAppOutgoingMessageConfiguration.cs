using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;

internal sealed class WhatsAppOutgoingMessageConfiguration : IEntityTypeConfiguration<WhatsAppOutgoingMessage>
{
    public void Configure(EntityTypeBuilder<WhatsAppOutgoingMessage> builder)
    {
        builder.ToTable("WhatsAppOutgoingMessages");
        builder.Property(item => item.Body).HasMaxLength(WhatsAppMessage.MaxBodyLength);
        builder.Property(item => item.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(item => item.WaMessageId).HasMaxLength(WhatsAppMessage.MaxWaMessageIdLength);
        builder.HasIndex(item => new { item.ContactId, item.OperatorUserId, item.RequestId }).IsUnique();
        builder.HasIndex(item => new { item.State, item.RequestedAtUtc, item.Id });
        builder.HasOne<WhatsAppContact>().WithMany().HasForeignKey(item => item.ContactId).OnDelete(DeleteBehavior.Restrict);
    }
}
