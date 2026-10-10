using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;

internal sealed class WhatsAppFlowOperationConfiguration : IEntityTypeConfiguration<WhatsAppFlowOperation>
{
    public void Configure(EntityTypeBuilder<WhatsAppFlowOperation> builder)
    {
        builder.ToTable("WhatsAppFlowOperations"); builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.Id, x.VersionId });
        builder.HasIndex(x => x.NeutralOperationId).IsUnique(); builder.HasIndex(x => x.VersionId).IsUnique();
        builder.Property(x => x.Fence).IsConcurrencyToken(); builder.Property(x => x.ProtectedManifest).HasColumnType("bytea");
        builder.HasIndex(x => new { x.NextCheckAtUtc, x.Id }).HasFilter("\"Completed\" = FALSE");
    }
}
