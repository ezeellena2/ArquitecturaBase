using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormRecordConfiguration : IEntityTypeConfiguration<BusinessFormRecord>
{
    public void Configure(EntityTypeBuilder<BusinessFormRecord> builder)
    {
        builder.ToTable("BusinessFormRecords", table =>
        {
            table.HasCheckConstraint("CK_BusinessFormRecords_Correction", "(\"ReplacesRecordId\" IS NULL) = (\"OriginalReceiptId\" IS NULL) AND (\"ReplacesRecordId\" IS NULL) = (\"CorrectedByUserId\" IS NULL) AND (\"ReplacesRecordId\" IS NULL) = (\"CorrectionReason\" IS NULL)");
            table.HasCheckConstraint("CK_BusinessFormRecords_Void", "(\"State\" = 'Voided') = (\"VoidedByUserId\" IS NOT NULL) AND (\"VoidedByUserId\" IS NULL) = (\"VoidedAtUtc\" IS NULL) AND (\"VoidedByUserId\" IS NULL) = (\"VoidReason\" IS NULL)");
        });
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.DefinitionId, value.Id });
        // A correction keeps the number of the response it replaces: the number is unique among the responses as they
        // arrived, and each one is replaced at most once, so a chain has a single current version.
        builder.HasIndex(value => new { value.DefinitionId, value.Number }).IsUnique().HasFilter("\"ReplacesRecordId\" IS NULL");
        builder.HasIndex(value => value.ReplacesRecordId).IsUnique().HasFilter("\"ReplacesRecordId\" IS NOT NULL");
        builder.HasIndex(value => value.ReplacedByRecordId).IsUnique().HasFilter("\"ReplacedByRecordId\" IS NOT NULL");
        builder.HasOne<BusinessFormRecord>().WithMany().HasForeignKey(value => value.ReplacesRecordId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessFormRecord>().WithMany().HasForeignKey(value => value.ReplacedByRecordId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(value => value.CorrectionReason).HasMaxLength(BusinessFormRecord.MaxReasonLength);
        builder.Property(value => value.VoidReason).HasMaxLength(BusinessFormRecord.MaxReasonLength);
        builder.HasIndex(value => value.SourceReceiptId).IsUnique();
        builder.Property(value => value.ValuesJson).HasColumnType("jsonb");
        builder.Property(value => value.CaptureOriginJson).HasColumnType("jsonb");
        builder.Property(value => value.PresentationCulture).HasMaxLength(2);
        builder.HasIndex(value => value.OriginRecordId);
        builder.HasOne<BusinessFormRecord>().WithMany().HasForeignKey(value => value.OriginRecordId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(32).HasDefaultValue(FormRecordState.Received).IsConcurrencyToken();
        builder.HasOne<BusinessFormRuleSet>().WithMany().HasForeignKey(value => new { value.DefinitionId, value.RuleSetId })
            .HasPrincipalKey(value => new { value.DefinitionId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessFormDefinition>().WithMany().HasForeignKey(value => value.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessFormVersion>().WithMany().HasForeignKey(value => new { value.DefinitionId, value.VersionId })
            .HasPrincipalKey(value => new { value.DefinitionId, value.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BusinessFormRecordValueConfiguration : IEntityTypeConfiguration<BusinessFormRecordValue>
{
    public void Configure(EntityTypeBuilder<BusinessFormRecordValue> builder)
    {
        builder.ToTable("BusinessFormRecordValues", table =>
        {
            table.HasCheckConstraint("CK_BusinessFormRecordValues_ExactlyOneValue",
                "(CASE WHEN \"TextValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"DecimalValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"DateValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"BooleanValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"ChoiceId\" IS NULL THEN 0 ELSE 1 END) = 1");
            table.HasCheckConstraint("CK_BusinessFormRecordValues_Position", "\"Position\" >= 0 AND \"Position\" < 20");
        });
        builder.HasKey(value => value.Id);
        builder.HasIndex(value => new { value.RecordId, value.FieldId, value.Position }).IsUnique();
        builder.Property(value => value.Position).HasDefaultValue(0);
        builder.Property(value => value.Type).HasMaxLength(32);
        builder.Property(value => value.TextValue).HasMaxLength(10000);
        builder.HasOne<BusinessFormRecord>().WithMany().HasForeignKey(value => new { value.DefinitionId, value.RecordId })
            .HasPrincipalKey(value => new { value.DefinitionId, value.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<BusinessFormField>().WithMany().HasForeignKey(value => new { value.DefinitionId, value.FieldId })
            .HasPrincipalKey(value => new { value.DefinitionId, value.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BusinessFormCaptureReceiptConfiguration : IEntityTypeConfiguration<BusinessFormCaptureReceipt>
{
    public void Configure(EntityTypeBuilder<BusinessFormCaptureReceipt> builder)
    {
        builder.ToTable("BusinessFormCaptureReceipts");
        builder.HasKey(value => value.Id);
        builder.HasIndex(value => value.SourceReceiptId).IsUnique();
        builder.HasIndex(value => new { value.DefinitionId, value.VersionId });
        builder.Property(value => value.Fingerprint).HasMaxLength(64);
        builder.Property(value => value.State).HasMaxLength(16);
        builder.Property(value => value.ErrorCode).HasMaxLength(128);
    }
}
