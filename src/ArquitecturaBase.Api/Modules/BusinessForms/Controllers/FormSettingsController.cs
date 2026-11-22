using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;

/// <summary>Configuración › Formularios (RC4-A): whoever manages the settings reads it and saves it with the revision read (409 otherwise).</summary>
[ApiController]
[Route("api/forms/settings")]
[Tags("Form settings")]
[HasPermission(Permissions.Settings.Manage)]
[RequestSizeLimit(8192)]
public sealed class FormSettingsController(IFormSettingsService service) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.GetAsync(cancellationToken)).ToActionResult(this);
    }

    [HttpPut]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromBody] UpdateFormSettingsHttpRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.UpdateAsync(new(request.ExpectedRevision, request.MaxFileSizeMiB, request.DailyRereadsPerAsker,
            request.SimilarDocumentsToSuggest, request.ProposedExpiryDays, request.DiscardedRetentionDays), cancellationToken)).ToActionResult(this);
    }
}
