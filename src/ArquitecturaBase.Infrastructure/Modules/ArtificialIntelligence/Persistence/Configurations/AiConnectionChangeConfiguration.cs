using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Configurations;
internal sealed class AiConnectionChangeConfiguration : IEntityTypeConfiguration<AiConnectionChange>
{
    public void Configure(EntityTypeBuilder<AiConnectionChange> builder)
    {
        builder.ToTable("AiConnectionChanges");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.OperatorId, x.RequestId }).IsUnique();
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Model).HasMaxLength(150);
        builder.Property(x => x.Fingerprint).HasMaxLength(44);
        builder.Property(x => x.KeyId).HasMaxLength(32);
    }
}
