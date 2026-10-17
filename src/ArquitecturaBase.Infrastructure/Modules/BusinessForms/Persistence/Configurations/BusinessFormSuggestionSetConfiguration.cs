using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;
internal sealed class BusinessFormSuggestionSetConfiguration : IEntityTypeConfiguration<BusinessFormSuggestionSet>
{
    public void Configure(EntityTypeBuilder<BusinessFormSuggestionSet> builder)
    {
        builder.ToTable("BusinessFormSuggestions");
        builder.HasKey(value=>value.Id);
        builder.HasAlternateKey(value=>new{value.DefinitionId,value.Id});
        builder.HasIndex(value=>new{value.DefinitionId,value.Revision}).IsUnique();
        builder.Property(value=>value.OriginalCulture).HasMaxLength(2);
        builder.Property(value=>value.SuggestionsJson).HasColumnType("jsonb");
        builder.HasOne<BusinessFormDefinition>().WithMany().HasForeignKey(value=>value.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessFormVersion>().WithMany().HasForeignKey(value=>new{value.DefinitionId,value.SourceVersionId})
            .HasPrincipalKey(value=>new{value.DefinitionId,value.Id}).OnDelete(DeleteBehavior.Restrict);
    }
}
