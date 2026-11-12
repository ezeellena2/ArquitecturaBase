using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.WhatsApp.Contracts;
using ArquitecturaBase.Api.Modules.WhatsApp.Routing;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.WhatsApp.Controllers;

/// <summary>Casos del asistente (AS1-A): the screen «Asistente», with «Configurar el asistente».</summary>
[ApiController]
[Route("api/whatsapp/assistant-agent")]
[Tags("WhatsApp assistant")]
[WhatsAppRoute(WhatsAppRouteFeature.Messaging)]
[HasPermission(WhatsAppPermissions.ConfigureAssistant)]
[EnableRateLimiting(WhatsAppApiRegistration.ChatReadRateLimitPolicy)]
[RequestSizeLimit(262144)]
public sealed class WhatsAppAssistantAgentController(IWhatsAppAssistantAgentService service, IWhatsAppAssistantAgentTestService tests) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<WhatsAppAssistantAgentResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) => (await service.GetAsync(cancellationToken)).ToActionResult(this);

    [HttpPut]
    [EnableRateLimiting(WhatsAppApiRegistration.ChatWriteRateLimitPolicy)]
    [ProducesResponseType<WhatsAppAssistantAgentResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromBody] UpdateWhatsAppAssistantAgentHttpRequest request, CancellationToken cancellationToken) =>
        (await service.UpdateAsync(WhatsAppAssistantAgentRequestMapping.Map(request), cancellationToken)).ToActionResult(this);

    /// <summary>Probar (AS3): what the assistant would do with what is saved, as an account linked to WhatsApp. Nothing is sent.</summary>
    [HttpPost("tests")]
    [EnableRateLimiting(WhatsAppApiRegistration.ChatWriteRateLimitPolicy)]
    [ProducesResponseType<WhatsAppAssistantAgentTestAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartTest([FromBody] StartWhatsAppAssistantAgentTestHttpRequest request, CancellationToken cancellationToken) =>
        (await tests.StartAsync(new(request.RequestId, request.ContactId, request.Messages), cancellationToken)).ToAcceptedResult(this);

    [HttpGet("tests/{requestId:guid}")]
    [ProducesResponseType<WhatsAppAssistantAgentTestResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTest(Guid requestId, [FromQuery] WhatsAppAssistantAgentTestQuery query, CancellationToken cancellationToken) =>
        (await tests.GetAsync(requestId, query.ContactId, cancellationToken)).ToActionResult(this);

    [HttpGet("test-recipients")]
    [ProducesResponseType<WhatsAppAssistantTestRecipientsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTestRecipients([FromQuery] WhatsAppAssistantTestRecipientsQuery query, CancellationToken cancellationToken) =>
        (await tests.ListRecipientsAsync(query.Search, cancellationToken)).ToActionResult(this);
}
