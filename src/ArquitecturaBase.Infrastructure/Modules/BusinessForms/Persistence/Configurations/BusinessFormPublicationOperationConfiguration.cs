using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormPublicationOperationConfiguration : IEntityTypeConfiguration<BusinessFormPublicationOperation>
{
    public void Configure(EntityTypeBuilder<BusinessFormPublicationOperation> builder)
    {
        builder.ToTable("BusinessFormPublicationOperations"); builder.HasKey(x => x.Id);
        builder.Ignore(x => x.IsOpen); builder.Property(x => x.State).HasConversion<string>().HasMaxLength(24);
        builder.Property(x => x.Stage).HasMaxLength(32); builder.Property(x => x.Fence).IsConcurrencyToken();
        builder.Property(x => x.ProtectedManifest).HasColumnType("bytea");
        builder.HasIndex(x => new { x.OperatorUserId, x.RequestId }).IsUnique();
        builder.HasIndex(x => x.VersionId).IsUnique();
        builder.HasIndex(x => x.DefinitionId).IsUnique().HasFilter("\"State\" NOT IN ('Published', 'Failed')");
        builder.HasOne<BusinessFormVersion>().WithMany().HasForeignKey(x => new { x.DefinitionId, x.VersionId })
            .HasPrincipalKey(x => new { x.DefinitionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessFormVersion>().WithMany().HasForeignKey(x => new { x.DefinitionId, x.BaseActiveVersionId })
            .HasPrincipalKey(x => new { x.DefinitionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
