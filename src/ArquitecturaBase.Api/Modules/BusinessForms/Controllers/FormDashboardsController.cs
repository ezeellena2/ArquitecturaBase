using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts.Dashboards;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
/// <summary>
/// The dashboard of a form (DOC7-A), with the permissions, the limits and the filter of the sheet: «Completar formularios» alone
/// sees its own responses, and the service narrows them.
/// </summary>
[ApiController]
[Route("api/forms/definitions/{id:guid}/dashboard")]
[Tags("Form responses")]
[HasAnyPermission(BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsCreate)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[RequestSizeLimit(524288)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormDashboardsController(IFormDashboardService service) : ControllerBase
{
    [HttpPost("search")]
    [ProducesResponseType<FormDashboardResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Search([FromRoute] Guid id, [FromBody] FormDashboardSearchHttpRequest request, CancellationToken cancellationToken) =>
        (await service.SearchAsync(id, new() { Filter = FormRecordsRequestMapping.Map(request.Filter), Period = request.Period }, cancellationToken))
            .ToActionResult(this);
}
