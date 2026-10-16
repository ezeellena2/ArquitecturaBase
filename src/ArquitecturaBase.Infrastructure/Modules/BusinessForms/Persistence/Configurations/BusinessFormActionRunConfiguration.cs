using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;
internal sealed class BusinessFormActionRunConfiguration:IEntityTypeConfiguration<BusinessFormActionRun>
{
    public void Configure(EntityTypeBuilder<BusinessFormActionRun> builder)
    {
        builder.ToTable("BusinessFormActionRuns");builder.HasKey(run=>run.Id);
        builder.HasAlternateKey(run=>new{run.RecordId,run.Id});
        builder.HasIndex(run=>new{run.RecordId,run.Event,run.RuleId,run.ActionId}).IsUnique();
        builder.HasIndex(run=>new{run.State,run.NextCheckAtUtc,run.AcceptedAtUtc});
        builder.Property(run=>run.State).HasConversion<string>().HasMaxLength(24);builder.Property(run=>run.Fence).IsConcurrencyToken();
        builder.Property(run=>run.Event).HasMaxLength(24);builder.Property(run=>run.Kind).HasMaxLength(32);builder.Property(run=>run.FailureCode).HasMaxLength(200);
        builder.Property(run=>run.SnapshotJson).HasColumnType("jsonb");builder.Property(run=>run.OutcomeJson).HasColumnType("jsonb");
        builder.HasOne<BusinessFormRecord>().WithMany().HasForeignKey(run=>new{run.DefinitionId,run.RecordId}).HasPrincipalKey(record=>new{record.DefinitionId,record.Id}).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessFormRuleSet>().WithMany().HasForeignKey(run=>new{run.DefinitionId,run.RuleSetId}).HasPrincipalKey(rules=>new{rules.DefinitionId,rules.Id}).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessFormActionRun>().WithMany().HasForeignKey(run=>new{run.RecordId,run.DependsOnRunId}).HasPrincipalKey(run=>new{run.RecordId,run.Id}).OnDelete(DeleteBehavior.Restrict);
    }
}
