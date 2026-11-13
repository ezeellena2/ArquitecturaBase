using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// Probar (AS3). It only submits and reads an operation of the engine in its own channel: no unit of work of the channel, no queue,
/// no WhatsApp limit. The tester needs «Configurar el asistente» and, because the model reads the real chat, to read WhatsApp chats
/// (the routes and the engine's context, H4), and the saved agent; the engine charges its own quota.
/// </summary>
internal sealed class WhatsAppAssistantAgentTestService(IWhatsAppAssistantAgentReader agents, IAssistantOperations operations, ICurrentUser currentUser,
    IRequestValidator validator, IPermissionService permissions, WhatsAppAssistantAgentTestPolicy tests, TimeProvider clock,
    ILogger<WhatsAppAssistantAgentTestService> logger) : IWhatsAppAssistantAgentTestService
{
    public const int MaxRecipients = 20;

    public Task<Result<WhatsAppAssistantTestRecipientsResponse>> ListRecipientsAsync(string? search, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppAssistantTestRecipientsResponse>>(logger, "ListWhatsAppAssistantTestRecipients", async () =>
        {
            if (currentUser.UserId is not { } actor) return WhatsAppAssistantErrors.SessionRequired;
            var text = search?.Trim();
            // The email of an account is searched and shown only to someone who can read accounts.
            var emails = await permissions.HasPermissionAsync(actor, Permissions.Users.Read, cancellationToken);
            var rows = await agents.ListTestRecipientsAsync(string.IsNullOrEmpty(text) ? null : text[..Math.Min(text.Length, 100)], emails, MaxRecipients,
                cancellationToken);
            return new WhatsAppAssistantTestRecipientsResponse(rows);
        });

    public Task<Result<WhatsAppAssistantAgentTestAcceptedResponse>> StartAsync(StartWhatsAppAssistantAgentTestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result<WhatsAppAssistantAgentTestAcceptedResponse>>(logger, "StartWhatsAppAssistantAgentTest", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError) return validationError;
            if (currentUser.UserId is not { } actor) return WhatsAppAssistantErrors.SessionRequired;
            if (await agents.FindMainAsync(cancellationToken) is null) return WhatsAppAssistantErrors.Unavailable;
            var context = await tests.ContextAsync(actor, request.ContactId, cancellationToken);
            if (context.IsFailure) return context.Error;
            var nowUtc = clock.GetUtcNow().UtcDateTime;
            // The test is the burst the account would send: one message per line, as WhatsApp would bring it.
            var submitted = await operations.SubmitAsync(new AssistantOperationRequest(request.RequestId, context.Value,
                string.Join("\n", request.Messages.Select(message => message.Trim())), nowUtc, nowUtc.AddDays(1)), cancellationToken);
            return submitted.IsFailure ? submitted.Error : new WhatsAppAssistantAgentTestAcceptedResponse(request.RequestId, "pending");
        });
    }

    public Task<Result<WhatsAppAssistantAgentTestResponse>> GetAsync(Guid requestId, Guid contactId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppAssistantAgentTestResponse>>(logger, "GetWhatsAppAssistantAgentTest", async () =>
        {
            if (requestId == Guid.Empty || contactId == Guid.Empty) return WhatsAppChatErrors.NotFound;
            if (currentUser.UserId is not { } actor) return WhatsAppAssistantErrors.SessionRequired;
            var context = await tests.ContextAsync(actor, contactId, cancellationToken);
            if (context.IsFailure) return context.Error;
            var operation = await operations.GetAsync(context.Value, requestId, cancellationToken);
            if (operation.IsFailure)
                return operation.Error.Type == ErrorType.NotFound ? WhatsAppChatErrors.NotFound
                    : new WhatsAppAssistantAgentTestResponse(requestId, "failed", operation.Error.Code, []);
            return operation.Value.State switch
            {
                "pending" or "running" => new WhatsAppAssistantAgentTestResponse(requestId, "pending", null, []),
                "completed" when await agents.FindMainAsync(cancellationToken) is { } agent =>
                    new WhatsAppAssistantAgentTestResponse(requestId, "completed", null, await tests.ActionsAsync(operation.Value, agent, context.Value, cancellationToken)),
                "completed" => new WhatsAppAssistantAgentTestResponse(requestId, "failed", WhatsAppAssistantErrors.Unavailable.Code, []),
                _ => new WhatsAppAssistantAgentTestResponse(requestId, "failed", operation.Value.FailureCode ?? WhatsAppAssistantErrors.Unavailable.Code, []),
            };
        });

}
