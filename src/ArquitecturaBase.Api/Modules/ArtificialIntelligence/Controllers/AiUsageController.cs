using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Controllers;

/// <summary>Consumo of the AI (PR12, PR13 and PR15): whoever sees its configuration sees its usage; whoever manages it loads the prices.</summary>
[ApiController]
[Route("api/ai/usage")]
[Tags("Artificial intelligence")]
[HasPermission(ArtificialIntelligencePermissions.Read)]
[EnableRateLimiting(ArtificialIntelligenceApiRegistration.ReadPolicy)]
[RequestSizeLimit(4096)]
public sealed class AiUsageController(IAiUsageService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AiUsageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] AiUsageQuery query, CancellationToken cancellationToken) =>
        (await service.GetMonthAsync(new(query.Month), cancellationToken)).ToActionResult(this);

    [HttpPut("prices")]
    [HasPermission(ArtificialIntelligencePermissions.Manage)]
    [EnableRateLimiting(ArtificialIntelligenceApiRegistration.WritePolicy)]
    [ProducesResponseType<AiModelPriceResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdatePrice([FromBody] UpdateAiModelPriceHttpRequest request, CancellationToken cancellationToken) =>
        (await service.UpdatePriceAsync(new(request.Provider, request.Model, request.InputUsdPerMillion, request.OutputUsdPerMillion,
            request.AudioUsdPerMinute, request.Revision), cancellationToken)).ToActionResult(this);
}
