using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
[ApiController]
[Route("api/forms/records/{recordId:guid}/files/{fileId:guid}")]
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.RecordsRead)]
public sealed class FormFilesController(IFormFileService service) : ControllerBase
{
    [HttpGet("thumbnail")]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "image/jpeg")]
    public Task<IActionResult> Thumbnail([FromRoute] Guid recordId, [FromRoute] Guid fileId, CancellationToken cancellationToken) =>
        OpenAsync(recordId, fileId, true, false, cancellationToken);

    [HttpGet("content")]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/octet-stream")]
    public Task<IActionResult> Content([FromRoute] Guid recordId, [FromRoute] Guid fileId, [FromQuery] FormFileContentQuery query, CancellationToken cancellationToken) =>
        OpenAsync(recordId, fileId, false, query.Download, cancellationToken);

    private async Task<IActionResult> OpenAsync(Guid recordId, Guid fileId, bool thumbnail, bool download, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        var result = await service.OpenAsync(recordId, fileId, thumbnail, download, cancellationToken);
        return result.IsFailure ? result.ToActionResult(this) : File(result.Value.Content, result.Value.ContentType, result.Value.DownloadName);
    }
}
