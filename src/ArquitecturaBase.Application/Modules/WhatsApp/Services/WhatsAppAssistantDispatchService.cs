using System.Text.Json;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppAssistantDispatchService(WhatsAppAssistantAccessPolicy access, WhatsAppAssistantSuggestionPolicy proposals,
    IWhatsAppAssistantReader reader, IWhatsAppAssistantRepository requests, IWhatsAppConversationRepository conversations,
    IUnitOfWork unitOfWork, WhatsAppAssistantAutomaticPolicy automatic, ILogger<WhatsAppAssistantDispatchService> logger) : IWhatsAppAssistantDispatchService
{
    public Task<Result<WhatsAppAssistantWorkResponse?>> PrepareAsync(Guid contactId, CancellationToken ct) =>
        OperationLog.RunAsync<Result<WhatsAppAssistantWorkResponse?>>(logger, "PrepareWhatsAppAssistant", () =>
            unitOfWork.ExecuteInTransactionAsync(token => PrepareCoreAsync(contactId, token), CommitPolicy.OnSuccess, ct));

    private async Task<Result<WhatsAppAssistantWorkResponse?>> PrepareCoreAsync(Guid contactId, CancellationToken ct)
    {
        var contact = await conversations.GetForUpdateAsync(contactId, ct);
        if (contact is null) return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        var choice = await reader.FindLatestChoiceAsync(contactId, ct);
        if (choice is not null)
        {
            var requestId = WhatsAppAssistantReplyCodec.TryParse(choice.ReplyId, out var parsedRequest, out _, out _) ? parsedRequest : Guid.Empty;
            var question = requestId == Guid.Empty ? null : await requests.GetByRequestAsync(requestId, ct);
            if (question?.ContactId == contactId && question.Kind == WhatsAppAssistantRequestKind.Automatic
                && question.State == WhatsAppAssistantRequestState.Ready && question.QuestionOutgoingId.HasValue && question.DefinitionId is null
                && await access.RevalidateAsync(contact, WhatsAppAssistantSuggestionPolicy.Reference(question), ct) is null
                && await reader.FindSourceAsync(contactId, question.SourceMessageId, ct) is {} original)
                return Result.Success<WhatsAppAssistantWorkResponse?>(Work(question, original, false));
            await automatic.MarkChoiceAsync(choice, contactId, ct);
            return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        }
        var row = await requests.GetPendingAsync(contactId, ct);
        if (row is null && contact.Mode == WhatsAppConversationMode.Assistant)
        {
            var source = await reader.FindLatestSourceAsync(contactId, ct);
            if (source is null || await requests.GetBySourceAsync(contactId, source.Id, WhatsAppAssistantRequestKind.Automatic, ct) is not null)
                return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            var captured = await access.CaptureAsync(contact, null, ct);
            if (captured.IsFailure || source.ContentExpiresAtUtc <= proposals.NowUtc) return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            row = WhatsAppAssistantRequest.Create(Guid.CreateVersion7(), contactId, source.Id, captured.Value.Context.ActorUserId,
                captured.Value.Context.SubjectUserId!.Value, WhatsAppAssistantRequestKind.Automatic, contact.ModeRevision, contact.LinkRevision,
                JsonSerializer.Serialize(captured.Value.Context, JsonSerializerOptions.Web), proposals.NowUtc, source.ContentExpiresAtUtc);
            requests.Add(row);
        }
        if (row is null) return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        if (await access.RevalidateAsync(contact, WhatsAppAssistantSuggestionPolicy.Reference(row), ct) is not null
            || await reader.FindLatestSourceAsync(contactId, ct) is not {} latest || latest.Id != row.SourceMessageId)
        {
            row.FinishWithoutReply(true, proposals.NowUtc);
            return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        }
        return Result.Success<WhatsAppAssistantWorkResponse?>(Work(row, latest, true));
    }

    private static WhatsAppAssistantWorkResponse Work(WhatsAppAssistantRequest row, WhatsAppAssistantSourceRow source, bool submit) =>
        new(row.RequestId, new(row.RequestId, WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row)),
            source.Body, source.OccurredAtUtc, row.ContentExpiresAtUtc)) { ShouldSubmit = submit };

    public Task<Result> CompleteAsync(Guid contactId, Guid requestId, Result<AssistantOperationResponse> outcome, CancellationToken ct) =>
        OperationLog.RunAsync<Result>(logger, "CompleteWhatsAppAssistant", () => CompleteCoreAsync(contactId, requestId, outcome, ct));

    private async Task<Result> CompleteCoreAsync(Guid contactId, Guid requestId, Result<AssistantOperationResponse> outcome, CancellationToken ct)
    {
        var original = await reader.FindAsync(contactId, requestId, ct);
        if (original is null) return WhatsAppChatErrors.NotFound;
        var choice = original.State == WhatsAppAssistantRequestState.Ready && original.QuestionOutgoingId.HasValue
            ? await reader.FindLatestChoiceAsync(contactId, ct) : null;
        Guid? selectedDefinition = null;
        Guid? selectedVersion = null;
        if (choice is not null && WhatsAppAssistantReplyCodec.TryParse(choice.ReplyId, out var chosenRequest, out var definition, out var version)
            && chosenRequest == requestId) { selectedDefinition = definition; selectedVersion = version; }
        if (original.State != WhatsAppAssistantRequestState.Pending && selectedDefinition is null) return Result.Success();
        if (outcome.IsSuccess && outcome.Value.RequestId != requestId) return WhatsAppAssistantErrors.InvalidProposal;
        Result<WhatsAppAssistantTerminalResponse?>? terminal = null;
        if (outcome.IsSuccess && outcome.Value.Proposal?.Capability is {Version:1,Id:"forms.catalog-filters.propose" or "forms.missing-request.propose"}
            && await reader.FindSourceAsync(contactId,original.SourceMessageId,ct) is {} terminalSource)
            terminal=await automatic.PrepareTerminalAsync(original,terminalSource,outcome.Value,ct);
        // Owner reads and account verification are outside the contact/request transaction.
        var observed = await proposals.ObserveAsync(original, outcome, ct, selectedDefinition);
        if (selectedDefinition.HasValue && (observed.IsSuccess && observed.Value.Prefill?.Form.VersionId != selectedVersion
            || outcome.IsSuccess && outcome.Value.Proposal?.Id != original.ProposalId)) observed = WhatsAppAssistantErrors.InvalidProposal;
        Result<WhatsAppFormSendContext?>? prepared = null;
        if (original.Kind == WhatsAppAssistantRequestKind.Automatic && observed.IsSuccess && observed.Value.Response.State == WhatsAppAssistantRequestState.Ready)
            prepared = await automatic.ResolveAsync(contactId, observed.Value, ct);
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var contact = await conversations.GetForUpdateAsync(contactId, token);
            var row = await requests.GetByRequestAsync(requestId, token);
            if (contact is null || row?.ContactId != contactId) return WhatsAppChatErrors.NotFound;
            if (row.State != WhatsAppAssistantRequestState.Pending && !(selectedDefinition.HasValue && row.State == WhatsAppAssistantRequestState.Ready))
                return Result.Success();
            if (selectedDefinition.HasValue)
            {
                var currentChoice = await reader.FindLatestChoiceAsync(contactId, token);
                if (choice is null || currentChoice?.Id != choice.Id || currentChoice.ReplyId != choice.ReplyId) return Result.Success();
                await automatic.MarkChoiceAsync(choice, contactId, token);
                if (await access.RevalidateAsync(contact, WhatsAppAssistantSuggestionPolicy.Reference(row), token) is not null
                    || observed.IsFailure || observed.Value.Prefill is null) return Result.Success();
            }
            else if (await access.RevalidateAsync(contact, WhatsAppAssistantSuggestionPolicy.Reference(row), token) is not null
                || await reader.FindLatestSourceAsync(contactId, token) is not {} latest || latest.Id != row.SourceMessageId || observed.IsFailure)
            {
                row.FinishWithoutReply(true, proposals.NowUtc);
                return Result.Success();
            }
            if (terminal is {IsSuccess:true,Value: {} effect})
            {
                automatic.AcceptTerminal(contact,row,effect);
                return Result.Success();
            }
            var view = observed.Value;
            if (view.Response.State == WhatsAppAssistantRequestState.Ready && view.Operation?.Proposal is {} proposal
                && (view.Prefill is not null || view.Clarification is not null))
            {
                if (row.Kind == WhatsAppAssistantRequestKind.Automatic)
                {
                    if (prepared is not { IsSuccess: true } context || (await automatic.AcceptAsync(contact, row, view, context.Value, token)).IsFailure)
                    {
                        if (selectedDefinition is null) row.FinishWithoutReply(true, proposals.NowUtc);
                    }
                    return Result.Success();
                }
                return row.Ready(proposal.Id, view.Prefill?.Form.DefinitionId, proposals.NowUtc) ? Result.Success() : WhatsAppAssistantErrors.StateConflict;
            }
            if(view.DeclinedDefinitionId.HasValue)
            {
                if(!await automatic.RecordDeclineAsync(row,view,token)) row.FinishWithoutReply(true,proposals.NowUtc);
                return Result.Success();
            }
            if (view.Response.State is WhatsAppAssistantRequestState.NoReply or WhatsAppAssistantRequestState.Blocked)
                row.FinishWithoutReply(view.Response.State == WhatsAppAssistantRequestState.Blocked, proposals.NowUtc);
            return Result.Success();
        }, CommitPolicy.OnSuccess, ct);
    }
}
