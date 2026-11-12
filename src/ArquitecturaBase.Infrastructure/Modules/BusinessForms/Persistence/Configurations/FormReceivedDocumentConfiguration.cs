using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

// The inbox «Documentos recibidos» (task 7): a channel source is registered once; a proposed document has its form and proposal, a
// confirmed one its response, and only a discarded or unclassified one a deletion date. The numbers to search by have a full-text index
// ('simple', prefix search), so they are searched in SQL without decrypting anything.
internal sealed class FormReceivedDocumentConfiguration : IEntityTypeConfiguration<FormReceivedDocument>
{
    public void Configure(EntityTypeBuilder<FormReceivedDocument> builder)
    {
        builder.ToTable("FormReceivedDocuments", table =>
        {
            table.HasCheckConstraint("CK_FormReceivedDocuments_Proposed",
                "\"State\" <> 'Proposed' OR (\"DefinitionId\" IS NOT NULL AND \"VersionId\" IS NOT NULL AND \"ProtectedProposal\" IS NOT NULL)");
            table.HasCheckConstraint("CK_FormReceivedDocuments_Confirmed", "(\"State\" = 'Confirmed') = (\"RecordId\" IS NOT NULL)");
            table.HasCheckConstraint("CK_FormReceivedDocuments_Deletion", "(\"State\" IN ('Discarded', 'Unclassified')) = (\"DeleteAfterUtc\" IS NOT NULL)");
            table.HasCheckConstraint("CK_FormReceivedDocuments_Review", "(\"ReviewedByUserId\" IS NULL) = (\"ReviewedAtUtc\" IS NULL)");
            // An extraction keeps its request, form, moment and who asked (with their language and zone) together.
            table.HasCheckConstraint("CK_FormReceivedDocuments_Extraction",
                "(\"ExtractionRequestId\" IS NULL) = (\"ExtractionDefinitionId\" IS NULL) AND (\"ExtractionRequestId\" IS NULL) = (\"ExtractionRequestedAtUtc\" IS NULL)"
                + " AND (\"ExtractionRequestId\" IS NULL) = (\"ExtractionActorUserId\" IS NULL) AND (\"ExtractionRequestId\" IS NULL) = (\"ExtractionCulture\" IS NULL)"
                + " AND (\"ExtractionRequestId\" IS NULL) = (\"ExtractionTimeZoneId\" IS NULL)");
        });
        builder.HasKey(value => value.Id);
        builder.Property(value => value.ChannelId).HasMaxLength(FormReceivedDocument.MaxChannelIdLength);
        builder.Property(value => value.SourceId).HasMaxLength(FormReceivedDocument.MaxSourceIdLength);
        builder.Property(value => value.StorageKey).HasMaxLength(FormReceivedDocument.MaxStorageKeyLength);
        builder.Property(value => value.FileName).HasMaxLength(FormReceivedDocument.MaxFileNameLength);
        builder.Property(value => value.MediaType).HasMaxLength(FormReceivedDocument.MaxMediaTypeLength);
        builder.Property(value => value.Sha256).HasColumnType("character(64)").HasMaxLength(64);
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(value => value.ReasonCode).HasMaxLength(FormReceivedDocument.MaxReasonCodeLength);
        builder.Property(value => value.DocumentType).HasMaxLength(FormReceivedDocument.MaxDocumentTypeLength);
        builder.Property(value => value.SearchText).HasMaxLength(FormReceivedDocument.MaxSearchTextLength);
        builder.Property(value => value.ExtractionCulture).HasMaxLength(2);
        builder.Property(value => value.ExtractionTimeZoneId).HasMaxLength(FormReceivedDocument.MaxTimeZoneIdLength);
        builder.HasIndex(value => new { value.ChannelId, value.SourceId }).IsUnique();
        builder.HasIndex(value => value.ReceivedAtUtc);
        builder.HasIndex(value => new { value.State, value.ReceivedAtUtc });
        builder.HasIndex(value => new { value.DefinitionId, value.Sha256 });
        builder.HasIndex(value => value.StorageKey);
        builder.HasIndex(value => value.SearchText).HasMethod("GIN").IsTsVectorExpressionIndex("simple");
        builder.HasIndex(value => value.DeleteAfterUtc).HasFilter("\"DeleteAfterUtc\" IS NOT NULL");
        builder.HasIndex(value => value.RecordId).IsUnique().HasFilter("\"RecordId\" IS NOT NULL");
        builder.HasOne<BusinessFormDefinition>().WithMany().HasForeignKey(value => value.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessFormVersion>().WithMany().HasForeignKey(value => new { value.DefinitionId, value.VersionId })
            .HasPrincipalKey(value => new { value.DefinitionId, value.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessFormRecord>().WithMany().HasForeignKey(value => value.RecordId).OnDelete(DeleteBehavior.Restrict);
    }
}
