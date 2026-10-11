using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormRetiredIdentityConfiguration : IEntityTypeConfiguration<BusinessFormRetiredIdentity>
{
    public void Configure(EntityTypeBuilder<BusinessFormRetiredIdentity> builder)
    {
        builder.ToTable("BusinessFormRetiredIdentities");
        builder.HasKey(identity => identity.Id);
        builder.Property(identity => identity.Kind).HasConversion<string>().HasMaxLength(8);
        builder.HasIndex(identity => new { identity.DefinitionId, identity.Kind, identity.IdentityId }).IsUnique();
        builder.HasOne<BusinessFormDefinition>().WithMany().HasForeignKey(identity => identity.DefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}
