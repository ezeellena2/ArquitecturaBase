using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Configurations;
internal sealed class AiKeyRingControlConfiguration : IEntityTypeConfiguration<AiKeyRingControl>
{
    public void Configure(EntityTypeBuilder<AiKeyRingControl> builder)
    {
        builder.ToTable("AiKeyRingControls");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.KeyId).IsUnique();
        builder.Property(x => x.KeyId).HasMaxLength(32);
        builder.Property(x => x.Control).HasMaxLength(44);
    }
}
