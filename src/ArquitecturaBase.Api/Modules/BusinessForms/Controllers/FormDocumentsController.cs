using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
[ApiController]
[Route("api/forms/records/{recordId:guid}/documents/{documentId:guid}")]
[Tags("Form documents")]
[HasPermission(BusinessFormsPermissions.RecordsRead)]
[EnableRateLimiting(BusinessFormsApiRegistration.FilesReadPolicy)]
public sealed class FormDocumentsController(IFormPdfDocumentService service) : ControllerBase
{
    [HttpGet("content")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/pdf")]
    public async Task<IActionResult> Content([FromRoute] Guid recordId, [FromRoute] Guid documentId, [FromQuery] FormFileContentQuery query, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store"; Response.Headers.XContentTypeOptions = "nosniff";
        var result = await service.GetAsync(recordId, documentId, cancellationToken);
        if (result.IsFailure) return result.ToActionResult(this);
        Response.Headers.ContentLanguage = result.Value.Culture;
        return File(result.Value.Content, "application/pdf", query.Download ? result.Value.Name : null);
    }
}
