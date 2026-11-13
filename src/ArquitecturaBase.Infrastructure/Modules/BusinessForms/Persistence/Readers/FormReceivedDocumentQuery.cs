using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Infrastructure.Persistence;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

// What the inbox reader and the reader of «Armar formulario con la IA» (DOC6) share without naming each other: a document as the inbox
// keeps it, and the unclassified documents that no draft built from documents includes (what a group of the inbox counts and a request for
// a topic takes).
internal static class FormReceivedDocumentQuery
{
    public static IQueryable<FormReceivedDocumentStoredRow> Rows(ApplicationDbContext db, IQueryable<FormReceivedDocument> documents) =>
        documents.Select(document => new FormReceivedDocumentStoredRow(document.Id, document.ChannelId, document.SourceId, document.SenderUserId,
            db.Users.Where(user => user.Id == document.SenderUserId).Select(user => user.DisplayName).FirstOrDefault(),
            document.ReceivedAtUtc, document.StorageKey, document.FileName, document.MediaType, document.Size, document.Sha256, document.Pages,
            document.ProtectedReading, document.ProtectedProposal, document.DefinitionId, document.VersionId,
            db.Set<BusinessFormVersion>().Where(version => version.Id == document.VersionId).Select(version => version.NameJson).FirstOrDefault(),
            db.Set<BusinessFormDefinition>().Where(definition => definition.Id == document.DefinitionId).Select(definition => definition.Prefix).FirstOrDefault(),
            document.State, document.ReasonCode, document.DocumentType, document.RecordId,
            db.Set<BusinessFormRecord>().Where(record => record.Id == document.RecordId).Select(record => (long?)record.Number).FirstOrDefault(),
            document.ReviewedByUserId, db.Users.Where(user => user.Id == document.ReviewedByUserId).Select(user => user.DisplayName).FirstOrDefault(),
            document.ReviewedAtUtc, document.ReplyExpiresAtUtc, document.NotSendable, document.DeleteAfterUtc, document.ExtractionRequestId,
            document.ExtractionDefinitionId, document.ExtractionRequestedAtUtc, document.Revision)
        {
            ExtractionActorUserId = document.ExtractionActorUserId, ExtractionCulture = document.ExtractionCulture,
            ExtractionTimeZoneId = document.ExtractionTimeZoneId,
        });

    // The requests whose draft still exists: a deleted one (the definitions' soft-delete filter) keeps neither its documents nor its topic.
    public static IQueryable<FormDocumentProposal> Drafted(ApplicationDbContext db) =>
        db.Set<FormDocumentProposal>().Where(proposal => db.Set<BusinessFormDefinition>().Any(definition => definition.Id == proposal.DefinitionId));

    public static IQueryable<FormReceivedDocument> Groupable(ApplicationDbContext db, IQueryable<FormReceivedDocument> documents)
    {
        var drafted = Drafted(db);
        return documents.Where(document => document.State == FormReceivedDocumentState.Unclassified
            && !drafted.Any(proposal => proposal.DocumentIds.Contains(document.Id)));
    }
}
