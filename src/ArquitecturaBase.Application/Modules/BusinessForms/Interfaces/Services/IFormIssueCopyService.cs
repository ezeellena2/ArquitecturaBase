using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// «SendCopy»: the copy by email of the document an earlier issuance of its rule left, to the receiver its issuer reads. It runs
/// outside any transaction; the run queues the email when it confirms it.
/// </summary>
public interface IFormIssueCopyService
{
    Task<FormActionOutcomeResponse> CopyAsync(FormActionWorkResponse work, CancellationToken cancellationToken);
}
