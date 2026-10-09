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
[ApiController]
[Route("api/ai/connection")]
[Tags("Artificial intelligence")]
[HasPermission(ArtificialIntelligencePermissions.Read)]
[EnableRateLimiting(ArtificialIntelligenceApiRegistration.ReadPolicy)]
[RequestSizeLimit(32768)]
public sealed class AiConnectionController(IAiConnectionService service, IAiConnectionTestService tester) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AiConnectionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => (await service.GetAsync(cancellationToken)).ToActionResult(this);
    [HttpPut]
    [HasPermission(ArtificialIntelligencePermissions.Manage)]
    [EnableRateLimiting(ArtificialIntelligenceApiRegistration.WritePolicy)]
    [ProducesResponseType<AiConnectionChangeResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromBody] UpdateAiConnectionHttpRequest request, CancellationToken cancellationToken) =>
        (await service.UpdateAsync(new(request.Provider, request.Model, request.CredentialChange, request.ApiKey,
            request.Enabled, request.ExpectedRevision, request.RequestId), cancellationToken)).ToActionResult(this);
    [HttpGet("changes/{requestId:guid}")]
    [HasPermission(ArtificialIntelligencePermissions.Manage)]
    [ProducesResponseType<AiConnectionChangeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChange([FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await service.GetChangeAsync(requestId, cancellationToken)).ToActionResult(this);
    [HttpPost("test")]
    [HasPermission(ArtificialIntelligencePermissions.Manage)]
    [EnableRateLimiting(ArtificialIntelligenceApiRegistration.TestPolicy)]
    [ProducesResponseType<AiConnectionTestResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Test([FromBody] TestAiConnectionHttpRequest request, CancellationToken cancellationToken) =>
        (await tester.TestAsync(new(request.Provider, request.Model, request.CredentialChange, request.ApiKey,
            request.ExpectedRevision), cancellationToken)).ToActionResult(this);
    [HttpPost("models")]
    [HasPermission(ArtificialIntelligencePermissions.Manage)]
    [EnableRateLimiting(ArtificialIntelligenceApiRegistration.WritePolicy)]
    [ProducesResponseType<AiModelsResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ListModels([FromBody] ListAiModelsHttpRequest request, CancellationToken cancellationToken) =>
        (await tester.ListModelsAsync(new(request.Provider, request.CredentialChange, request.ApiKey,
            request.ExpectedRevision), cancellationToken)).ToActionResult(this);
}
