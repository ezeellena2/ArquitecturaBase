using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

// DOC6 (task 14): the requests of «Armar formulario con la IA», their documents as the inbox keeps them, the unclassified documents of a
// topic that no draft built from documents includes yet (also what the groups of the inbox count) and what the re-reading after publishing
// has to do; without tracking, filtered in SQL before ordering.
internal sealed class FormDocumentProposalReader(ApplicationDbContext dbContext) : IFormDocumentProposalReader
{
    private const string ReadingOn = """{"enabled":true}""";

    public Task<FormDocumentProposalStoredRow?> FindByRequestAsync(Guid actorUserId, Guid requestId, CancellationToken cancellationToken) =>
        Rows(dbContext.Set<FormDocumentProposal>().AsNoTracking().Where(proposal => proposal.ActorUserId == actorUserId && proposal.RequestId == requestId))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<FormDocumentProposalStoredRow?> FindAsync(Guid proposalId, CancellationToken cancellationToken) =>
        Rows(dbContext.Set<FormDocumentProposal>().AsNoTracking().Where(proposal => proposal.Id == proposalId)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<FormReceivedDocumentStoredRow>> ListDocumentsAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken) =>
        await FormReceivedDocumentQuery.Rows(dbContext, dbContext.Set<FormReceivedDocument>().AsNoTracking().Where(document => documentIds.Contains(document.Id)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListTopicDocumentIdsAsync(Guid topicId, int maximum, CancellationToken cancellationToken) =>
        await (from document in FormReceivedDocumentQuery.Groupable(dbContext, dbContext.Set<FormReceivedDocument>().AsNoTracking())
               join request in dbContext.Set<BusinessFormMissingRequest>().AsNoTracking() on document.Id equals request.SourceReceiptId
               where request.TopicId == topicId
               orderby document.ReceivedAtUtc descending, document.Id descending
               select document.Id).Take(maximum).ToListAsync(cancellationToken);

    // A draft from documents whose active version is published in a channel with its reading on, not yet read again for it or with documents
    // still pending for it, when nothing holds it: neither its own round or wait nor another request of the same person (their rounds go one
    // at a time; listed anyway, a blocked request would be reserved again every round for nothing and the oldest blocked would fill a batch).
    public async Task<IReadOnlyList<Guid>> ListRereadsDueAsync(DateTime nowUtc, int maximum, CancellationToken cancellationToken) =>
        await (from proposal in dbContext.Set<FormDocumentProposal>().AsNoTracking()
               join version in FormOfferableVersionQuery.ChannelVersions(dbContext) on proposal.DefinitionId equals version.DefinitionId
               where version.DocumentReadingJson != null && EF.Functions.JsonContains(version.DocumentReadingJson, ReadingOn)
                   && (proposal.RereadVersionId == null || proposal.RereadVersionId != version.Id || proposal.PendingRereadIds.Length > 0)
                   && (proposal.PendingRereadIds.Length == 0 || proposal.RereadAfterUtc == null || proposal.RereadAfterUtc <= nowUtc)
                   && !dbContext.Set<FormDocumentProposal>().Any(other => other.ActorUserId == proposal.ActorUserId && other.Id != proposal.Id
                       && other.PendingRereadIds.Length > 0 && other.RereadAfterUtc > nowUtc)
               orderby proposal.RequestedAtUtc, proposal.Id
               select proposal.Id).Take(maximum).ToListAsync(cancellationToken);

    // The documents of a request already read again whose extraction for its form still runs.
    public async Task<IReadOnlyList<Guid>> ListRereadingDocumentIdsAsync(int maximum, CancellationToken cancellationToken) =>
        await dbContext.Set<FormReceivedDocument>().AsNoTracking()
            .Where(document => document.ExtractionRequestId != null && dbContext.Set<FormDocumentProposal>().Any(proposal => proposal.RereadVersionId != null
                && proposal.DefinitionId == document.ExtractionDefinitionId && proposal.DocumentIds.Contains(document.Id)))
            .OrderBy(document => document.ExtractionRequestedAtUtc).ThenBy(document => document.Id).Select(document => document.Id).Take(maximum)
            .ToListAsync(cancellationToken);

    private IQueryable<FormDocumentProposalStoredRow> Rows(IQueryable<FormDocumentProposal> proposals) =>
        proposals.Select(proposal => new FormDocumentProposalStoredRow(proposal.Id, proposal.RequestId, proposal.ActorUserId, proposal.Culture,
            proposal.TimeZoneId, proposal.RequestedAtUtc, proposal.TopicId,
            dbContext.Set<BusinessFormMissingTopic>().Where(topic => topic.Id == proposal.TopicId).Select(topic => topic.Name).FirstOrDefault(),
            proposal.DocumentIds, proposal.DefinitionId, proposal.ProposalId, proposal.RereadVersionId));
}
