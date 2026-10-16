using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;
internal sealed class BusinessFormRuleSetConfiguration : IEntityTypeConfiguration<BusinessFormRuleSet>
{
    public void Configure(EntityTypeBuilder<BusinessFormRuleSet> builder)
    {
        builder.ToTable("BusinessFormRuleSets");
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.DefinitionId, value.Id });
        builder.HasIndex(value => new { value.DefinitionId, value.Revision }).IsUnique();
        builder.Property(value => value.OriginalCulture).HasMaxLength(2);
        builder.Property(value => value.RulesJson).HasColumnType("jsonb");
        builder.Property(value => value.QuestionSchemaJson).HasColumnType("jsonb");
        builder.HasOne<BusinessFormDefinition>().WithMany().HasForeignKey(value => value.DefinitionId).OnDelete(DeleteBehavior.Restrict);
    }
}
