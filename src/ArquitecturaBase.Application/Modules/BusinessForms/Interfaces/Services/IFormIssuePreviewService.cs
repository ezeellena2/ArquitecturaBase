using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// «¿La emito?» before «Enviar» (EM2-A): what a form that issues would issue from the answers not sent yet, with the same checks
/// as the send and nothing saved.
/// </summary>
public interface IFormIssuePreviewService
{
    Task<Result<FormIssuePreviewResponse>> PreviewAsync(Guid definitionId, PreviewFormIssueRequest request, CancellationToken cancellationToken);
}
