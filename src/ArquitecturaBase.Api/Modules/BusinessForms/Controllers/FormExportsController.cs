using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
[ApiController]
[Route("api/forms/definitions/{id:guid}/records/export")]
[Tags("Form responses")]
[HasPermission(BusinessFormsPermissions.RecordsRead)]
[HasPermission(BusinessFormsPermissions.RecordsExport)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[RequestSizeLimit(524288)]
public sealed class FormExportsController(IFormExportService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export([FromRoute] Guid id, [FromBody] FormExportHttpRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        var result = await service.ExportAsync(id, new(FormResponsesController.Map(request.Filter), request.Columns), cancellationToken);
        return result.IsFailure ? result.ToActionResult(this) : File(result.Value.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.Value.FileName);
    }
}
