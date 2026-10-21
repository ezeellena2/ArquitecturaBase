using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

internal sealed class BusinessFormTaskConfiguration : IEntityTypeConfiguration<BusinessFormTask>
{
    public void Configure(EntityTypeBuilder<BusinessFormTask> builder)
    {
        builder.ToTable("BusinessFormTasks"); builder.HasKey(task => task.Id);
        builder.Property(task => task.Reason).HasMaxLength(500);
        builder.HasIndex(task => new { task.DefinitionId, task.RoleId, task.RequestedAtUtc, task.Id }).HasFilter("\"State\" = 'Open'");
        builder.HasIndex(task => new { task.RoleId, task.State, task.RequestedAtUtc, task.Id });
        builder.HasIndex(task => task.RunId).IsUnique(); builder.HasIndex(task => task.RecordId).IsUnique().HasFilter("\"State\" = 'Open'");
        builder.Property(task => task.State).HasConversion<string>().HasMaxLength(24); builder.Property(task => task.Revision).IsConcurrencyToken();
        builder.HasOne<BusinessFormRecord>().WithMany().HasForeignKey(task => new { task.DefinitionId, task.RecordId }).HasPrincipalKey(record => new { record.DefinitionId, record.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessFormRuleSet>().WithMany().HasForeignKey(task => new { task.DefinitionId, task.RuleSetId }).HasPrincipalKey(rules => new { rules.DefinitionId, rules.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessFormActionRun>().WithMany().HasForeignKey(task => new { task.RecordId, task.RunId }).HasPrincipalKey(run => new { run.RecordId, run.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
