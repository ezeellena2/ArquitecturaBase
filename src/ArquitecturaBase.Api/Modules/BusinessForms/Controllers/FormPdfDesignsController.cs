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
[Route("api/forms/definitions/{id:guid}/pdf-design")]
[Tags("Form PDF designs")]
[HasPermission(BusinessFormsPermissions.RulesManage)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[RequestSizeLimit(131072)]
public sealed class FormPdfDesignsController(IFormPdfDesignService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FormPdfDesignResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.GetCurrentAsync(id, cancellationToken)).ToActionResult(this);
    }
    [HttpPost]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormPdfDesignResponse>(StatusCodes.Status201Created)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save([FromRoute] Guid id, [FromBody] SaveFormPdfDesignHttpRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.SaveAsync(id, new(request.ExpectedRevision, request.Design), cancellationToken)).ToCreatedResult(this, nameof(Get), value => new { id = value.DefinitionId });
    }
    [HttpPost("preview")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview([FromRoute] Guid id, [FromBody] PreviewFormPdfDesignHttpRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store"; Response.Headers.XContentTypeOptions = "nosniff";
        var result = await service.PreviewAsync(id, new(request.Design, request.Culture), cancellationToken);
        if (result.IsFailure) return result.ToActionResult(this);
        Response.Headers.ContentLanguage = result.Value.Culture;
        return File(result.Value.Content, "application/pdf");
    }
}
