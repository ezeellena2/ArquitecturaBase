using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Configurations;
internal sealed class AiConnectionConfiguration : IEntityTypeConfiguration<AiConnection>
{
    public void Configure(EntityTypeBuilder<AiConnection> builder)
    {
        builder.ToTable("AiConnections", table => table.HasCheckConstraint("CK_AiConnections_Scope", "\"Scope\" = 1"));
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Scope).IsUnique();
        builder.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Model).HasMaxLength(150);
        builder.Property(x => x.ProtectedApiKey).HasMaxLength(6000);
        builder.Property(x => x.Revision).IsConcurrencyToken();
    }
}
