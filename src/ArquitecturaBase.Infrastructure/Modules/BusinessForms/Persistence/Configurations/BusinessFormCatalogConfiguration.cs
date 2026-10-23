using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormCatalogConfiguration : IEntityTypeConfiguration<BusinessFormCatalog>
{
    public void Configure(EntityTypeBuilder<BusinessFormCatalog> builder)
    {
        builder.ToTable("BusinessFormCatalogs", table =>
        {
            table.HasCheckConstraint("CK_BusinessFormCatalogs_LastImport", "(\"LastImportSha256\" IS NULL) = (\"LastImportAtUtc\" IS NULL)");
            table.HasCheckConstraint("CK_BusinessFormCatalogs_ManualFields", "\"Kind\" = 'Manual' OR (\"FieldsJson\" IS NULL AND \"PresentationJson\" IS NULL)");
            table.HasCheckConstraint("CK_BusinessFormCatalogs_ValueType",
                "\"ValueType\" IN ('Text','Integer','Decimal','Date') AND (\"Kind\" = 'Manual' OR \"ValueType\" = 'Text')");
            table.HasCheckConstraint("CK_BusinessFormCatalogs_KindFilters",
                "(\"Kind\" = 'Manual' AND \"FiltersJson\" IS NULL) OR (\"Kind\" = 'Filters' AND \"FiltersJson\" IS NOT NULL)");
            table.HasCheckConstraint("CK_BusinessFormCatalogs_Proposal", """
                ("ProposalId" IS NULL AND "ProposalEvidenceJson" IS NULL AND "ProposalState" IS NULL
                    AND "ProposalDecidedByUserId" IS NULL AND "ProposalDecidedAtUtc" IS NULL)
                OR ("Kind" = 'Filters' AND "ProposalId" IS NOT NULL AND "ProposalEvidenceJson" IS NOT NULL AND "ProposalState" IS NOT NULL AND (
                    ("ProposalState" = 'Pending' AND NOT "IsActive" AND "ProposalDecidedByUserId" IS NULL AND "ProposalDecidedAtUtc" IS NULL)
                    OR ("ProposalState" = 'Approved' AND "ProposalDecidedByUserId" IS NOT NULL AND "ProposalDecidedAtUtc" IS NOT NULL)
                    OR ("ProposalState" = 'Discarded' AND NOT "IsActive" AND "ProposalDecidedByUserId" IS NOT NULL AND "ProposalDecidedAtUtc" IS NOT NULL)))
                """);
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Key).HasMaxLength(32);
        builder.HasIndex(value => value.Key).IsUnique();
        builder.Property(value => value.NameJson).HasColumnType("jsonb");
        builder.Property(value => value.DescriptionJson).HasColumnType("jsonb");
        builder.Property(value => value.FiltersJson).HasColumnType("jsonb");
        builder.Property(value => value.FieldsJson).HasColumnType("jsonb");
        builder.Property(value => value.PresentationJson).HasColumnType("jsonb");
        builder.Property(value => value.ProposalEvidenceJson).HasColumnType("jsonb");
        builder.Property(value => value.ProposalState).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(value => value.ProposalId).IsUnique();
        builder.Property<string>("DisplayNameEs").HasMaxLength(120)
            .HasComputedColumnSql("COALESCE(NULLIF(\"NameJson\" ->> 'es', ''), NULLIF(\"NameJson\" ->> 'en', ''), '')", stored: true);
        builder.Property<string>("DisplayNameEn").HasMaxLength(120)
            .HasComputedColumnSql("COALESCE(NULLIF(\"NameJson\" ->> 'en', ''), NULLIF(\"NameJson\" ->> 'es', ''), '')", stored: true);
        builder.Property<string>("DisplayDescriptionEs").HasMaxLength(1000)
            .HasComputedColumnSql("COALESCE(NULLIF(\"DescriptionJson\" ->> 'es', ''), NULLIF(\"DescriptionJson\" ->> 'en', ''), '')", stored: true);
        builder.Property<string>("DisplayDescriptionEn").HasMaxLength(1000)
            .HasComputedColumnSql("COALESCE(NULLIF(\"DescriptionJson\" ->> 'en', ''), NULLIF(\"DescriptionJson\" ->> 'es', ''), '')", stored: true);
        builder.Property(value => value.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.ValueType).HasConversion<string>().HasMaxLength(16).HasDefaultValue(BusinessFormCatalogValueType.Text);
        builder.Property(value => value.LastImportSha256).HasColumnType("character(64)").HasMaxLength(64);
        builder.Property(value => value.LastImportAtUtc);
        builder.Property(value => value.Revision).IsConcurrencyToken();
    }
}
