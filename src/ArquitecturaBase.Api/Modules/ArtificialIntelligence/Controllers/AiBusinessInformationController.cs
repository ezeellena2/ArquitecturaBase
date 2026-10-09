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
[Route("api/ai/business-information")]
[Tags("Artificial intelligence")]
[HasPermission(ArtificialIntelligencePermissions.BusinessRead)]
[EnableRateLimiting(ArtificialIntelligenceApiRegistration.ReadPolicy)]
[RequestSizeLimit(262144)]
public sealed class AiBusinessInformationController(IAiBusinessInformationService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AiBusinessInformationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => (await service.GetAsync(cancellationToken)).ToActionResult(this);
    [HttpPut]
    [HasPermission(ArtificialIntelligencePermissions.BusinessManage)]
    [EnableRateLimiting(ArtificialIntelligenceApiRegistration.WritePolicy)]
    [ProducesResponseType<AiBusinessInformationChangeResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromBody] UpdateAiBusinessInformationHttpRequest request, CancellationToken cancellationToken) =>
        (await service.UpdateAsync(new(request.Name, request.Description, request.Hours, request.Location, request.Services,
            request.Policies, request.Faq, request.ExpectedRevision, request.RequestId, MapDetails(request.Details)), cancellationToken)).ToActionResult(this);
    [HttpGet("changes/{requestId:guid}")]
    [HasPermission(ArtificialIntelligencePermissions.BusinessManage)]
    [ProducesResponseType<AiBusinessInformationChangeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChange([FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await service.GetChangeAsync(requestId, cancellationToken)).ToActionResult(this);

    private static AiBusinessDetails? MapDetails(AiBusinessDetailsHttpRequest? value) => value is null ? null : new(
        value.Location is { } location ? new(location.Mode, location.Address, location.Reference, location.Area, location.Latitude, location.Longitude, location.PlaceId) : null!,
        value.OpeningHours is { } hours ? new(hours.TimeZoneId,
            hours.Days?.Select(x => x is null ? null! : new BusinessOpeningDay(x.Weekday, x.Status, MapPeriods(x.Periods))).ToArray()!,
            hours.Exceptions?.Select(x => x is null ? null! : new BusinessSpecialDate(x.Date, x.Status, MapPeriods(x.Periods))).ToArray()!) : null!,
        value.Faq?.Select(x => x is null ? null! : new BusinessFaqEntry(x.Id, x.Question, x.Answer)).ToArray()!);
    private static BusinessOpeningPeriod[] MapPeriods(IReadOnlyList<BusinessOpeningPeriodHttpRequest>? periods) =>
        periods?.Select(x => x is null ? null! : new BusinessOpeningPeriod(x.Start, x.End, x.NextDay)).ToArray()!;
}
