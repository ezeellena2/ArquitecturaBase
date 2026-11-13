using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

// DOC6 (task 14): a request of «Armar formulario con la IA» is unique by who asked and its UUID; its documents are kept as an array of the
// inbox's ids, in the order they were attached (a document the purge deleted just stops being found), and the draft it created points to
// its definition. A version read again is only possible once there is a draft.
internal sealed class FormDocumentProposalConfiguration : IEntityTypeConfiguration<FormDocumentProposal>
{
    public void Configure(EntityTypeBuilder<FormDocumentProposal> builder)
    {
        builder.ToTable("FormDocumentProposals", table =>
            table.HasCheckConstraint("CK_FormDocumentProposals_Reread", "\"RereadVersionId\" IS NULL OR \"DefinitionId\" IS NOT NULL"));
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Culture).HasMaxLength(2);
        builder.Property(value => value.TimeZoneId).HasMaxLength(FormDocumentProposal.MaxTimeZoneIdLength);
        builder.HasIndex(value => new { value.ActorUserId, value.RequestId }).IsUnique();
        builder.HasIndex(value => value.DefinitionId).HasFilter("\"DefinitionId\" IS NOT NULL");
        builder.HasIndex(value => value.TopicId).HasFilter("\"TopicId\" IS NOT NULL");
        builder.HasOne<BusinessFormDefinition>().WithMany().HasForeignKey(value => value.DefinitionId).OnDelete(DeleteBehavior.Restrict);
    }
}
