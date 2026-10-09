using ArquitecturaBase.Domain.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

internal sealed class QueuedDeliveryConfiguration : IEntityTypeConfiguration<QueuedDelivery>
{
    public void Configure(EntityTypeBuilder<QueuedDelivery> builder)
    {
        builder.ToTable("QueuedDeliveries");
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.Channel).HasMaxLength(64);
        builder.Property(delivery => delivery.MessageType).HasMaxLength(128);
        builder.HasIndex(delivery => new { delivery.Channel, delivery.ClaimedAtUtc, delivery.CreatedAtUtc });
        builder.HasIndex(delivery => delivery.ExpiresAtUtc);
    }
}
