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

// The draft of a proposal from the Home chat: created once, by the person who asked for it.
[ApiController]
[Route("api/forms/definitions/assistant-proposals")]
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.Read)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[RequestSizeLimit(524288)]
public sealed class FormDefinitionAssistantProposalsController(IFormDraftProposalService service) : ControllerBase
{
    [HttpGet("{proposalId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<FormDraftAssistantProposalResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromRoute] Guid proposalId, CancellationToken cancellationToken) =>
        (await service.GetAsync(proposalId, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [HasPermission(BusinessFormsPermissions.Manage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormDraftAssistantProposalResponse>(StatusCodes.Status201Created)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateFromAssistantProposalHttpRequest request, CancellationToken cancellationToken) =>
        (await service.CreateAsync(new(request.RequestId, request.ProposalId), cancellationToken))
            .ToCreatedResult(this, nameof(Get), value => new { proposalId = value.ProposalId });
}
