using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
/// <summary>The dashboard of a form (DOC7-A), with the permissions of the sheet: «Ver respuestas», or «Completar formularios» on one's own.</summary>
public interface IFormDashboardService
{
    Task<Result<FormDashboardResponse>> SearchAsync(Guid id, FormDashboardRequest request, CancellationToken cancellationToken);
}
