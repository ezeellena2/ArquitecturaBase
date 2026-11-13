using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// DOC6, after publishing: the documents a form was built from are read again for it, one interpretation of <c>forms-documents</c> each with
/// the form fixed, out of the publication's unit of work. The worker lists what is due, reserves a request for its form's published version
/// (its pending documents, held for one round so only one host reads them), starts each document's extraction
/// (<see cref="IFormReceivedDocumentExtractionService"/>), records what became of it and applies the ones that finished; what the engine
/// could not take is tried again in a later round.
/// </summary>
public interface IFormDocumentRereadService
{
    /// <summary>The requests to reserve and the documents whose reading for their form still runs, a batch of each.</summary>
    Task<FormDocumentRereadDueResponse> ListDueAsync(CancellationToken cancellationToken);
    /// <summary>The request's pending documents for its form's published version, held for one round; null when nothing is due for it.</summary>
    Task<Result<FormDocumentRereadWorkResponse?>> ReserveAsync(Guid proposalId, CancellationToken cancellationToken);
    /// <summary>
    /// What became of one document: read again, or left as it is, it leaves the pending ones; when the engine cannot take it now, all of them
    /// wait until it says. True while the round can go on with the next one: not once the engine cannot take more now, nor once a newer
    /// version started over (what the engine took still counts).
    /// </summary>
    Task<Result<bool>> RecordAsync(FormDocumentRereadOutcomeRequest request, CancellationToken cancellationToken);
}
