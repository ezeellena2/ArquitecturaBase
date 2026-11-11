using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// «¿La emito?» (EM2-A): whoever loaded a response from a channel confirms its issuance or not, from the detail of the app or with
/// a button of the channel. Issuing releases the run; not issuing voids the response with «No se emitió» and nothing is issued.
/// After a summary that failed (decision 4), going on is «Volver a probar»: <c>retry</c> says the person saw that failure, and the
/// run asks for the summary again. A decision on a summary other than the one the run is waiting with conflicts.
/// </summary>
public interface IFormIssueConfirmationService
{
    Task<Result<FormWorkflowResponse>> ConfirmAsync(Guid recordId, Guid runId, bool issue, bool retry, CancellationToken cancellationToken);

    /// <summary>
    /// The same decision from a button of the channel, in the name of the person who pressed it; a stale one is ignored. Not
    /// issuing returns what the channel answers that person, in their language («No la emitimos. FAC-000012 quedó anulada.»);
    /// issuing or trying again returns no text.
    /// </summary>
    Task<Result<string?>> ReplyAsync(Guid runId, Guid actorUserId, bool issue, bool retry, CancellationToken cancellationToken);
}
