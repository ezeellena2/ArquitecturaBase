using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;

internal sealed class WhatsAppFlowBindingConfiguration : IEntityTypeConfiguration<WhatsAppFlowBinding>
{
    public void Configure(EntityTypeBuilder<WhatsAppFlowBinding> builder)
    {
        builder.ToTable("WhatsAppFlowBindings"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Culture).HasMaxLength(2); builder.Property(x => x.TechnicalName).HasMaxLength(80);
        builder.Property(x => x.MetaFlowId).HasMaxLength(128); builder.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Step).HasConversion<string>().HasMaxLength(16); builder.Property(x => x.Fence).IsConcurrencyToken();
        builder.Property(x => x.ProtectedProjection).HasColumnType("bytea"); builder.Property(x => x.DiagnosticsJson).HasColumnType("jsonb");
        builder.Property(x => x.FailureCode).HasMaxLength(80);
        builder.HasIndex(x => new { x.VersionId, x.Culture }).IsUnique();
        builder.HasIndex(x => x.MetaFlowId).IsUnique().HasFilter("\"MetaFlowId\" IS NOT NULL");
        builder.HasOne<WhatsAppFlowOperation>().WithMany().HasForeignKey(x => new { x.OperationId, x.VersionId })
            .HasPrincipalKey(x => new { x.Id, x.VersionId }).OnDelete(DeleteBehavior.Restrict);
    }
}
