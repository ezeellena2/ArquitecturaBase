using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;
internal sealed class BusinessFormDraftChangeConfiguration : IEntityTypeConfiguration<BusinessFormDraftChange>
{
    public void Configure(EntityTypeBuilder<BusinessFormDraftChange> builder)
    {
        builder.ToTable("FormDraftChanges"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasMaxLength(6); builder.Property(x => x.ProtectedResult).HasColumnType("bytea");
        builder.HasIndex(x => new { x.OperatorUserId, x.RequestId }).IsUnique();
        builder.HasIndex(x => new { x.ResultExpiresAtUtc, x.Id }).HasFilter("\"ProtectedResult\" IS NOT NULL");
        builder.HasOne<BusinessFormDefinition>().WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
    }
}
