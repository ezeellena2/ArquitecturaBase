using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// The «IssueDocument» action: it hands the bound data of the response to its installed issuer once, applies a person's decision
/// on an issuance in review and tells where the issuance stands. It runs outside any transaction; the run confirms it afterwards.
/// </summary>
public interface IFormIssueActionService
{
    Task<FormActionOutcomeResponse> ExecuteAsync(FormActionWorkResponse work, CancellationToken cancellationToken);
}
