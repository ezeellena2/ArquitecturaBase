using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

// RE1-A: one result per run, sealed in the response it belongs to; it goes with the response.
internal sealed class BusinessFormRecordResultConfiguration : IEntityTypeConfiguration<BusinessFormRecordResult>
{
    public void Configure(EntityTypeBuilder<BusinessFormRecordResult> builder)
    {
        builder.ToTable("BusinessFormRecordResults");
        builder.HasKey(result => result.Id);
        builder.HasIndex(result => result.RunId).IsUnique();
        builder.HasIndex(result => new { result.DefinitionId, result.Source, result.RecordedAtUtc });
        builder.Property(result => result.Source).HasMaxLength(BusinessFormRecordResult.MaxSourceLength);
        builder.Property(result => result.SourceNameJson).HasColumnType("jsonb");
        builder.Property(result => result.ValuesJson).HasColumnType("jsonb");
        builder.HasOne<BusinessFormRecord>().WithMany().HasForeignKey(result => new { result.DefinitionId, result.RecordId })
            .HasPrincipalKey(record => new { record.DefinitionId, record.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<BusinessFormActionRun>().WithMany().HasForeignKey(result => new { result.RecordId, result.RunId })
            .HasPrincipalKey(run => new { run.RecordId, run.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
