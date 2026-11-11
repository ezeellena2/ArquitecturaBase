using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// «¿La emito?» (EM2-A) from the detail of the app: whoever loaded a response from a channel confirms its issuance or not. Issuing
/// releases the run; not issuing voids the response with «No se emitió» and nothing is issued. After a summary that failed
/// (decision 4), going on is «Volver a probar»: <c>retry</c> says the person saw that failure, and the run asks for the summary
/// again. A decision on a summary other than the one the run is waiting with, or on a response that no longer stands, conflicts.
/// The same decision from a button of a channel arrives by <c>IStructuredRecordActionReplySink</c>.
/// </summary>
public interface IFormIssueConfirmationService
{
    Task<Result<FormWorkflowResponse>> ConfirmAsync(Guid recordId, Guid runId, bool issue, bool retry, CancellationToken cancellationToken);
}
