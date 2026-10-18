using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Configurations;

internal sealed class AiAssistantRequestReceiptConfiguration : IEntityTypeConfiguration<AiAssistantRequestReceipt>
{
    public void Configure(EntityTypeBuilder<AiAssistantRequestReceipt> builder)
    {
        builder.ToTable("AiAssistantRequestReceipts");
        builder.HasKey(receipt => receipt.Id);
        builder.Property(receipt => receipt.Fingerprint).HasMaxLength(44);
        builder.Property(receipt => receipt.KeyId).HasMaxLength(32);
        builder.Property(receipt => receipt.ChannelId).HasMaxLength(80);
        builder.HasIndex(receipt => new { receipt.OwnerUserId, receipt.RequestId }).IsUnique();
        builder.HasIndex(receipt => new { receipt.AcceptedDayUtc, receipt.OwnerUserId, receipt.ChannelId, receipt.IsAudio });
        builder.HasIndex(receipt => receipt.ExpiresAtUtc);
        // No FK to content: deduplication and acceptance quotas survive conversation removal and payload purge.
    }
}
