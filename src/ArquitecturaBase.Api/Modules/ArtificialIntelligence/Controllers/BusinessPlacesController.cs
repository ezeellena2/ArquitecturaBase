using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Controllers;
[ApiController]
[Route("api/ai/business-information/places")]
[Tags("Artificial intelligence")]
[HasPermission(ArtificialIntelligencePermissions.BusinessRead)]
[HasPermission(ArtificialIntelligencePermissions.BusinessManage)]
[EnableRateLimiting(ArtificialIntelligenceApiRegistration.PlacesPolicy)]
[RequestSizeLimit(4096)]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class BusinessPlacesController(IBusinessPlacesService service) : ControllerBase
{
    [HttpPost("search")]
    [ProducesResponseType<BusinessPlacesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromBody] SearchBusinessPlacesHttpRequest request, CancellationToken cancellationToken) =>
        (await service.SearchAsync(new(request.Input), cancellationToken)).ToActionResult(this);
}
