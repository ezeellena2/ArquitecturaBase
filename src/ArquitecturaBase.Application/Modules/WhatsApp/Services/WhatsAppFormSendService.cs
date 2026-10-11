using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed partial class WhatsAppFormSendService(
    WhatsAppFormSendPolicy policy, WhatsAppFlowSessionIssuer sessionIssuer, IWhatsAppFlowSessionRepository sessions,
    IWhatsAppConversationRepository conversations, IRequestValidator validator, ICurrentUser currentUser,
    IUnitOfWork unitOfWork, ILogger<WhatsAppFormSendService> logger) : IWhatsAppFormSendService
{
    private Guid Actor => currentUser.UserId ?? throw new InvalidOperationException("The form sender requires an authenticated operator.");

    public Task<Result<WhatsAppOutgoingMessageResponse>> SendAsync(SendWhatsAppFormRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppOutgoingMessageResponse>>(logger, "SendWhatsAppForm",
            () => SendCoreAsync(request, cancellationToken));

    private async Task<Result<WhatsAppOutgoingMessageResponse>> SendCoreAsync(SendWhatsAppFormRequest request,
        CancellationToken cancellationToken)
    {
        if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
        var fingerprint = Fingerprint(request, Actor);
        var existing = await conversations.GetByRequestAsync(request.ContactId, Actor, request.RequestId, cancellationToken);
        if (existing is not null) return await ReplayAsync(existing, fingerprint, cancellationToken);

        var contextResult = await policy.ResolveAsync(request, Actor, cancellationToken);
        if (contextResult.IsFailure) return contextResult.Error;
        var context = contextResult.Value;
        var body = string.IsNullOrWhiteSpace(request.Message)
            ? FlowTexts.Get("CompleteForm", context.Recipient.Culture!)
            : request.Message.Trim();

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var contact = await conversations.GetForUpdateAsync(request.ContactId, ct);
            if (contact is null) return Result.Failure<WhatsAppOutgoingMessageResponse>(WhatsAppChatErrors.NotFound);
            if (contact.ModeRevision != request.ExpectedModeRevision)
                return Result.Failure<WhatsAppOutgoingMessageResponse>(WhatsAppChatErrors.RevisionConflict);
            if (contact.Mode != WhatsAppConversationMode.Manual)
                return Result.Failure<WhatsAppOutgoingMessageResponse>(WhatsAppChatErrors.ManualRequired);
            var destination = policy.ValidateContact(contact);
            if (destination.IsFailure) return Result.Failure<WhatsAppOutgoingMessageResponse>(destination.Error);

            var revalidated = await policy.RevalidateAsync(context, request, Actor, ct);
            if (revalidated.IsFailure) return Result.Failure<WhatsAppOutgoingMessageResponse>(revalidated.Error);
            var concurrent = await conversations.GetByRequestAsync(request.ContactId, Actor, request.RequestId, ct);
            if (concurrent is not null) return await ReplayAsync(concurrent, fingerprint, ct);
            if (await conversations.ExistsUnresolvedOutgoingAsync(contact.Id, ct))
                return Result.Failure<WhatsAppOutgoingMessageResponse>(WhatsAppChatErrors.SendPending);

            var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var issue = sessionIssuer.Create(context, request, Actor, fingerprint, token);
            var session = issue.Session;
            sessions.Add(session);
            var outgoing = WhatsAppOutgoingMessage.CreateFlow(Actor, contact.Id, request.RequestId, body, session.Id, issue.CreatedAtUtc);
            outgoing.AttachFlowSession(session);
            conversations.Add(outgoing);
            return Result.Success(ToResponse(outgoing, session));
        }, CommitPolicy.OnSuccess, cancellationToken);
    }

    private async Task<Result<WhatsAppOutgoingMessageResponse>> ReplayAsync(WhatsAppOutgoingMessage outgoing,
        string fingerprint, CancellationToken cancellationToken)
    {
        if (outgoing.Kind != WhatsAppOutgoingMessageKind.Flow || outgoing.FlowSessionId is not { } sessionId)
            return WhatsAppChatErrors.RequestConflict;
        var session = await sessions.GetAsync(sessionId, cancellationToken);
        return session?.RequestFingerprint == fingerprint
            ? Result.Success(ToResponse(outgoing, session))
            : Result.Failure<WhatsAppOutgoingMessageResponse>(WhatsAppChatErrors.RequestConflict);
    }

    private static string Fingerprint(SendWhatsAppFormRequest request, Guid actor)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Format = 1, Actor = actor, request.ContactId, request.RequestId, request.DefinitionId, request.VersionId,
            request.ExpectedModeRevision, request.ExpectedRecipientContextRevision, Message = request.Message?.Trim(),
        });
        try { return Convert.ToHexStringLower(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static WhatsAppOutgoingMessageResponse ToResponse(WhatsAppOutgoingMessage outgoing, WhatsAppFlowSession session)
    {
        var name = JsonSerializer.Deserialize<Dictionary<string, string>>(session.NameJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The stored Flow form name is invalid.");
        return new(outgoing.Id, outgoing.RequestId, outgoing.OperatorUserId, outgoing.Body, outgoing.RequestedAtUtc,
            outgoing.State, outgoing.WaMessageId,
            new(session.DefinitionId, session.VersionId, session.VersionNumber, name, session.Culture),
            Kind: WhatsAppOutgoingMessageKind.Flow);
    }
}
