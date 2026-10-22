using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;

[ApiController]
[Route("api/forms/catalogs/{id:guid}")]
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.CatalogsRead)]
[EnableRateLimiting(BusinessFormsApiRegistration.ExportPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormCatalogTransfersController(IFormCatalogImportService imports, IFormCatalogExportService exports) : ControllerBase
{
    private const long MaximumRequestBytes = FormCatalogImportLimits.MaximumFileBytes + 64 * 1024;

    [HttpPost("import-previews")]
    [HasPermission(BusinessFormsPermissions.CatalogsManage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumRequestBytes)]
    [ProducesResponseType<FormCatalogImportPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Preview([FromRoute] Guid id, [FromForm] FormCatalogImportHttpRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        using var content = request.File.OpenReadStream();
        return (await imports.PreviewAsync(id, Map(request, content), cancellationToken)).ToActionResult(this);
    }

    [HttpPost("imports")]
    [HasPermission(BusinessFormsPermissions.CatalogsManage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumRequestBytes)]
    [ProducesResponseType<FormCatalogSaveResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Apply([FromRoute] Guid id, [FromForm] FormCatalogImportHttpRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        using var content = request.File.OpenReadStream();
        return (await imports.ApplyAsync(id, Map(request, content), cancellationToken)).ToActionResult(this);
    }

    [HttpGet("export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Export([FromRoute] Guid id, [FromQuery] FormCatalogExportQuery query, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        var result = await exports.ExportAsync(id, query.Template, cancellationToken);
        return result.IsFailure ? result.ToActionResult(this)
            : File(result.Value.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", result.Value.FileName);
    }

    private static FormCatalogImportRequest Map(FormCatalogImportHttpRequest request, Stream content) => new(request.ExpectedRevision,
        request.Mode, content, request.File.FileName, request.SheetSha256, request.ConfirmDeactivations, request.ConfirmEmpty, request.ConfirmErrors);
}
