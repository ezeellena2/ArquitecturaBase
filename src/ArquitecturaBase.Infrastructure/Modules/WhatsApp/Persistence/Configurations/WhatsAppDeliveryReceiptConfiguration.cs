using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;

internal sealed class WhatsAppDeliveryReceiptConfiguration : IEntityTypeConfiguration<WhatsAppDeliveryReceipt>
{
    public void Configure(EntityTypeBuilder<WhatsAppDeliveryReceipt> builder)
    {
        builder.ToTable("WhatsAppDeliveryReceipts");
        builder.Property(item => item.WaMessageId).HasMaxLength(WhatsAppMessage.MaxWaMessageIdLength);
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(item => item.WaMessageId).IsUnique();
    }
}
