using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;
internal sealed class BusinessFormDefinitionConfiguration : IEntityTypeConfiguration<BusinessFormDefinition>
{
    public void Configure(EntityTypeBuilder<BusinessFormDefinition> builder)
    {
        builder.ToTable("FormDefinitions", table =>
        {
            table.HasCheckConstraint("CK_FormDefinitions_Deactivation", "(\"DeactivatedAtUtc\" IS NULL) = (\"DeactivatedBy\" IS NULL)");
            table.HasCheckConstraint("CK_FormDefinitions_Proposal", "(\"ProposalId\" IS NULL) = (\"ProposalEvidenceJson\" IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DeactivatedAtUtc).IsRequired(false);
        builder.Property(x => x.DeactivatedBy).IsRequired(false);
        builder.Property(x => x.Name).HasMaxLength(120); builder.Property(x => x.Prefix).HasMaxLength(12);
        builder.HasIndex(x => x.Prefix).IsUnique(); builder.Property(x => x.Culture).HasMaxLength(2);
        builder.Property(x => x.NameJson).HasColumnType("jsonb");
        builder.Property(x => x.OfferWhen).HasMaxLength(1000);
        // A proposal creates one draft at most; a deleted draft keeps its proposal reserved, like its prefix.
        builder.HasIndex(x => x.ProposalId).IsUnique();
        builder.Property(x => x.ProposalEvidenceJson).HasColumnType("jsonb");
        // BI1-A: the standard of the library a form came from.
        builder.Property(x => x.StandardKey).HasMaxLength(BusinessFormDefinition.MaxStandardKeyLength);
        builder.Property(x => x.StandardBindingsJson).HasColumnType("jsonb");
        builder.HasIndex(x => x.StandardKey);
        // SQL search and order use the same authoring name as the requested UI language.
        // These projections are generated from the single name map; they are never edited independently.
        builder.Property<string>("DisplayNameEs").HasMaxLength(120)
            .HasComputedColumnSql("COALESCE(NULLIF(\"NameJson\" ->> 'es', ''), \"Name\")", stored: true);
        builder.Property<string>("DisplayNameEn").HasMaxLength(120)
            .HasComputedColumnSql("COALESCE(NULLIF(\"NameJson\" ->> 'en', ''), \"Name\")", stored: true);
        builder.Property(x => x.SchemaJson).HasColumnType("jsonb");
        builder.Property(x => x.SubmitLabelJson).HasColumnType("jsonb");
        // DOC1: the reading of documents of the draft, null without one.
        builder.Property(x => x.DocumentReadingJson).HasColumnType("jsonb");
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.Property(x => x.LastRecordNumber).IsConcurrencyToken();
        builder.Property(x => x.ActiveVersionId).IsConcurrencyToken();
        builder.Property(x => x.ActiveRuleSetId).IsConcurrencyToken();
        builder.Property(x => x.ActiveSuggestionSetId).IsConcurrencyToken();
        builder.HasOne<BusinessFormSuggestionSet>().WithMany().HasForeignKey(x => new { x.Id, x.ActiveSuggestionSetId })
            .HasPrincipalKey(x => new { x.DefinitionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.ActivePdfDesignId).IsConcurrencyToken();
        builder.HasOne<BusinessFormPdfDesign>().WithMany().HasForeignKey(x => new { x.Id, x.ActivePdfDesignId })
            .HasPrincipalKey(x => new { x.DefinitionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        // The active rule set belongs to this definition: (Id, ActiveRuleSetId) references BusinessFormRuleSets (DefinitionId, Id)
        // with a foreign key the migration DeferActiveRuleSetKey creates in SQL as DEFERRABLE INITIALLY DEFERRED, out of the EF
        // model. A new definition and its first rule set point at each other and go in one save («Agregar» from the library):
        // with the relationship in the model EF cannot order those two inserts, and Postgres checks the key at commit instead.
        // EnsureCreated does not create it, so its tests run on a migrated database. Do not model it again.
        builder.HasOne<BusinessFormVersion>().WithMany().HasForeignKey(x => new { x.Id, x.ActiveVersionId })
            .HasPrincipalKey(x => new { x.DefinitionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
