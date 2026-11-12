using System.Globalization;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Validation;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// Casos del asistente (AS1-A): the screen «Asistente» reads the main agent with the forms it can send, the knowledge and whether
/// the assistant can run, and saves the four tabs at once with the revision it read (409 if someone saved first).
/// </summary>
internal sealed class WhatsAppAssistantAgentService(IWhatsAppAssistantAgentRepository agents, IWhatsAppAssistantAgentReader reader,
    IRequestValidator validator, IUnitOfWork unitOfWork, IStructuredFormSource forms, IBusinessKnowledgeSource knowledge,
    IAssistantOperations operations, ILogger<WhatsAppAssistantAgentService> logger) : IWhatsAppAssistantAgentService
{
    public Task<Result<WhatsAppAssistantAgentResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppAssistantAgentResponse>>(logger, "GetWhatsAppAssistantAgent", () => ReadAsync(cancellationToken));

    public Task<Result<WhatsAppAssistantAgentResponse>> UpdateAsync(UpdateWhatsAppAssistantAgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result<WhatsAppAssistantAgentResponse>>(logger, "UpdateWhatsAppAssistantAgent", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError) return validationError;
            var saved = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await agents.LockAsync(ct);
                var agent = await agents.GetMainAsync(ct);
                if (agent is null)
                {
                    if (request.ExpectedRevision != 0) return Result.Failure(WhatsAppAssistantAgentErrors.RevisionConflict);
                    agent = WhatsAppAssistantAgent.Create(request.OriginalCulture);
                    agents.Add(agent);
                }
                else if (agent.Revision != request.ExpectedRevision || agent.OriginalCulture != request.OriginalCulture)
                {
                    return Result.Failure(WhatsAppAssistantAgentErrors.RevisionConflict);
                }

                var updated = agent.Update(request.Profile, request.Cases, request.Fallback, request.Limits);
                if (updated.IsFailure) return Result.Failure(updated.Error);
                // What the model reads of the agent has to fit one read of the engine: the save says so, never a conversation.
                return WhatsAppAssistantAgentViewPolicy.Fits(new(agent.Revision, agent.OriginalCulture, agent.Profile, agent.Cases, agent.Fallback, agent.Limits))
                    ? Result.Success() : Result.Failure(FieldErrors.Validation("cases", WhatsAppAssistantAgentValidationMessages.TooLong));
            }, CommitPolicy.OnSuccess, cancellationToken);
            return saved.IsFailure ? saved.Error : await ReadAsync(cancellationToken);
        });
    }

    private async Task<Result<WhatsAppAssistantAgentResponse>> ReadAsync(CancellationToken cancellationToken)
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
        var row = await reader.FindMainAsync(cancellationToken);
        var defaults = WhatsAppAssistantAgent.Create(culture);
        var formRows = await forms.ListAssistantFormsAsync(culture, cancellationToken);
        var businessKnowledge = await knowledge.ReadAsync(culture, cancellationToken);
        var availability = await operations.GetAvailabilityAsync(cancellationToken);
        return new WhatsAppAssistantAgentResponse(row?.Revision ?? 0, row?.OriginalCulture ?? culture, row?.Profile ?? defaults.Profile,
            row?.Cases ?? defaults.Cases, row?.Fallback ?? defaults.Fallback, row?.Limits ?? defaults.Limits,
            [.. formRows.Select(form => new WhatsAppAssistantFormResponse(form.DefinitionId, form.Name, form.OfferWhen))], businessKnowledge, availability);
    }
}
