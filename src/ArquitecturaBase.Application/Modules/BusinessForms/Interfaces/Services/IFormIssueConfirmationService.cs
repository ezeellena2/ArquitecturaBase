using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// «¿La emito?» (EM2-A): whoever loaded a response from a channel confirms its issuance or not, from the detail of the app or with
/// a button of the channel. Issuing releases the run; not issuing voids the response with «No se emitió» and nothing is issued.
/// </summary>
public interface IFormIssueConfirmationService
{
    Task<Result<FormWorkflowResponse>> ConfirmAsync(Guid recordId, Guid runId, bool issue, CancellationToken cancellationToken);

    /// <summary>The same decision from a button of the channel, in the name of the person who pressed it; a stale one is ignored.</summary>
    Task<Result> ReplyAsync(Guid runId, Guid actorUserId, bool issue, CancellationToken cancellationToken);
}
