using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;
internal sealed class BusinessFormPdfDesignConfiguration : IEntityTypeConfiguration<BusinessFormPdfDesign>
{
    public void Configure(EntityTypeBuilder<BusinessFormPdfDesign> builder)
    {
        builder.ToTable("BusinessFormPdfDesigns"); builder.HasKey(design => design.Id);
        builder.Property(design => design.OriginalCulture).HasMaxLength(2);
        builder.Property(design => design.DesignJson).HasColumnType("jsonb");
        builder.HasIndex(design => new { design.DefinitionId, design.Revision }).IsUnique();
        builder.HasAlternateKey(design => new { design.DefinitionId, design.Id });
        builder.HasOne<BusinessFormDefinition>().WithMany().HasForeignKey(design => design.DefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}
