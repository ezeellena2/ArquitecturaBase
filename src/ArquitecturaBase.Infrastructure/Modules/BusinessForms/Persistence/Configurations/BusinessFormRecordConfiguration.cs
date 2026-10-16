using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormRecordConfiguration : IEntityTypeConfiguration<BusinessFormRecord>
{
    public void Configure(EntityTypeBuilder<BusinessFormRecord> builder)
    {
        builder.ToTable("BusinessFormRecords");
        builder.HasKey(value => value.Id);
        builder.HasAlternateKey(value => new { value.DefinitionId, value.Id });
        builder.HasIndex(value => new { value.DefinitionId, value.Number }).IsUnique();
        builder.HasIndex(value => value.SourceReceiptId).IsUnique();
        builder.Property(value => value.ValuesJson).HasColumnType("jsonb");
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
        builder.ToTable("BusinessFormRecordValues", table => table.HasCheckConstraint("CK_BusinessFormRecordValues_ExactlyOneValue",
            "(CASE WHEN \"TextValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"DecimalValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"DateValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"BooleanValue\" IS NULL THEN 0 ELSE 1 END + CASE WHEN \"ChoiceId\" IS NULL THEN 0 ELSE 1 END) = 1"));
        builder.HasKey(value => value.Id);
        builder.HasIndex(value => new { value.RecordId, value.FieldId }).IsUnique();
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
