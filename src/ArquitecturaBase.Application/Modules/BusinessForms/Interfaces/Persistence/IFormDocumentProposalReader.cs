using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>
/// Reads for «Armar formulario con la IA» (DOC6), without tracking: a request by its owner and UUID or by its id, its documents as the inbox
/// keeps them, the unclassified documents of a topic that no draft was built from yet, and what the re-reading of the published forms has to
/// do. Fixed queries, bounded in SQL.
/// </summary>
public interface IFormDocumentProposalReader
{
    Task<FormDocumentProposalStoredRow?> FindByRequestAsync(Guid actorUserId, Guid requestId, CancellationToken cancellationToken);
    Task<FormDocumentProposalStoredRow?> FindAsync(Guid proposalId, CancellationToken cancellationToken);
    /// <summary>The inbox documents with these ids (one that was deleted is left out), in no particular order.</summary>
    Task<IReadOnlyList<FormReceivedDocumentStoredRow>> ListDocumentsAsync(IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken);
    /// <summary>
    /// The unclassified documents whose «Otro» noted that topic and that no draft built from documents includes, the newest first, up to
    /// <paramref name="maximum"/>.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListTopicDocumentIdsAsync(Guid topicId, int maximum, CancellationToken cancellationToken);
    /// <summary>
    /// The requests whose draft has an active version published in a channel with its reading on that their documents were not read again
    /// for, or that still have documents pending for it, when nothing holds them at <paramref name="nowUtc"/>: neither their round or the
    /// engine nor another request of the same person (their rounds go one at a time); the oldest first, up to <paramref name="maximum"/>.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListRereadsDueAsync(DateTime nowUtc, int maximum, CancellationToken cancellationToken);
    /// <summary>The documents of a request already read again whose extraction for its form still runs, the oldest first, up to <paramref name="maximum"/>.</summary>
    Task<IReadOnlyList<Guid>> ListRereadingDocumentIdsAsync(int maximum, CancellationToken cancellationToken);
}
