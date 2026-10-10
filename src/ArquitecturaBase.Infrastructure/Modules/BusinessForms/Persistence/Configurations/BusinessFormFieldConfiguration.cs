using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormFieldConfiguration : IEntityTypeConfiguration<BusinessFormField>
{
    public void Configure(EntityTypeBuilder<BusinessFormField> builder)
    {
        builder.ToTable("BusinessFormFields"); builder.HasKey(x => x.Id);
        builder.Property(x => x.StableKey).HasMaxLength(38); builder.Property(x => x.Type).HasMaxLength(16);
        builder.HasIndex(x => new { x.DefinitionId, x.StableKey }).IsUnique();
        builder.HasOne<BusinessFormDefinition>().WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
    }
}
