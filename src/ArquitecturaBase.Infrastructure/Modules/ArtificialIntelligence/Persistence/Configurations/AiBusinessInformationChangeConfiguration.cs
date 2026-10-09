using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Configurations;
internal sealed class AiBusinessInformationChangeConfiguration : IEntityTypeConfiguration<AiBusinessInformationChange>
{
    public void Configure(EntityTypeBuilder<AiBusinessInformationChange> builder)
    {
        builder.ToTable("AiBusinessInformationChanges");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.OperatorId, x.RequestId }).IsUnique();
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.Property(x => x.Fingerprint).HasMaxLength(44);
        builder.Property(x => x.KeyId).HasMaxLength(32);
    }
}
