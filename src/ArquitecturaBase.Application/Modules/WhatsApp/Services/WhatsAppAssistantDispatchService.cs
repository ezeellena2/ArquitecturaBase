using System.Text.Json;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
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
        OperationLog.RunAsync<Result<WhatsAppAssistantWorkResponse?>>(logger, "PrepareWhatsAppAssistant", async () =>
        {
            WhatsAppAssistantReferenceRow? superseded = null;
            var result = await unitOfWork.ExecuteInTransactionAsync(token => PrepareCoreAsync(contactId, row => superseded = row, token), CommitPolicy.OnSuccess, ct);
            if (result.IsSuccess && superseded is not null) await proposals.CancelAsync(superseded, ct);
            return result;
        });

    private async Task<Result<WhatsAppAssistantWorkResponse?>> PrepareCoreAsync(Guid contactId, Action<WhatsAppAssistantReferenceRow> supersede, CancellationToken ct)
    {
        var contact = await conversations.GetForUpdateAsync(contactId, ct);
        if (contact is null) return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        // Casos del asistente: a tap on a case's button with a fixed text or a handoff is answered before anything else.
        if (await reader.FindLatestCaseTapAsync(contactId, ct) is { } caseTap && await automatic.ApplyCaseTapAsync(contact, caseTap, ct))
            return Result.Success<WhatsAppAssistantWorkResponse?>(null);
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
        var row=await requests.GetPendingAsync(contactId,ct);
        var recovering = row is not null;
        var latest=await reader.FindLatestSourceAsync(contactId,ct);
        if(row is null && latest?.CapturedRecordId is {} recordId)
            return await PrepareCapturedAsync(contact,latest,recordId,ct);
        if(row is null && contact.Mode==WhatsAppConversationMode.Assistant)
        {
            // A text whose spreadsheet is still downloading waits for it (IA, Stage 2).
            if(latest is null || latest.AssistantProcessedAtUtc is not null || latest.State==WhatsAppAssistantSourceState.Waiting
                || await requests.GetBySourceAsync(contactId,latest.Id,WhatsAppAssistantRequestKind.Automatic,ct) is not null)
                return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            // Casos del asistente (ráfagas): with an agent the turn waits until the person stops writing.
            if(await automatic.WaitsForBurstAsync(contactId,latest.OccurredAtUtc,ct))
                return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            // After «Cambiar algo» or a question this message is the answer: the engine continues that operation (IA3-A).
            var previous=await reader.FindChangeRequestAsync(contactId,ct);
            // A spreadsheet without a request is only that answer, and a text whose spreadsheet was rejected was already answered.
            var interpretable=latest.State==WhatsAppAssistantSourceState.Ready || latest.State==WhatsAppAssistantSourceState.Alone && previous is not null;
            var captured=interpretable?await access.CaptureAsync(contact,null,ct):null;
            if(captured is not {IsSuccess:true} || latest.ContentExpiresAtUtc<=proposals.NowUtc)
            {
                await automatic.MarkSourceAsync(latest,ct);
                return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            }
            row=WhatsAppAssistantRequest.Create(Guid.CreateVersion7(),contactId,latest.Id,captured.Value.Context.ActorUserId,
                captured.Value.Context.SubjectUserId!.Value,WhatsAppAssistantRequestKind.Automatic,contact.ModeRevision,contact.LinkRevision,
                JsonSerializer.Serialize(captured.Value.Context,JsonSerializerOptions.Web),proposals.NowUtc,latest.ContentExpiresAtUtc);
            if(previous is {} change) row.ContinueFrom(change);
            if(latest.Spreadsheet is {} spreadsheet) row.CarryAttachment(spreadsheet.MessageId);
            requests.Add(row);
        }
        if(row is null)return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        var revoked=await access.RevalidateAsync(contact,WhatsAppAssistantSuggestionPolicy.Reference(row),ct) is not null;
        if(revoked || !MatchesSource(row,latest))
        {
            supersede(WhatsAppAssistantSuggestionPolicy.Reference(row));
            // A newer message replaced it before it answered: the message that replaces it continues what it continued.
            if(revoked) row.FinishWithoutReply(true,proposals.NowUtc); else row.Replace(proposals.NowUtc);
            return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        }
        var work = Work(row, latest!, true);
        if (recovering && row.Kind == WhatsAppAssistantRequestKind.Automatic && row.SourceRecordId.HasValue && !row.IsAlways
            && !await automatic.HasOfferCapacityAsync(contact.Id, WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row)), ct))
            work = work with { RecoverOnly = true };
        return Result.Success<WhatsAppAssistantWorkResponse?>(work);
    }

    private async Task<Result<WhatsAppAssistantWorkResponse?>> PrepareCapturedAsync(WhatsAppContact contact,WhatsAppAssistantSourceRow source,Guid recordId,CancellationToken ct)
    {
        if(source.AssistantProcessedAtUtc is not null || await requests.GetByCapturedRecordAsync(contact.Id,recordId,ct) is not null)return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        var culture=await access.CaptureConfiguredCultureAsync(contact,ct);
        if(culture.IsFailure || source.ContentExpiresAtUtc<=proposals.NowUtc)
        {
            await automatic.MarkSourceAsync(source,ct);
            return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        }
        var configured=await proposals.ListConfiguredAsync(contact.UserId!.Value,culture.Value,recordId,ct);
        if(configured.IsFailure)
        {
            if(configured.Error.Type is ErrorType.Validation or ErrorType.Unauthorized or ErrorType.Forbidden or ErrorType.NotFound or ErrorType.Gone)
                await automatic.MarkSourceAsync(source,ct);
            return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        }
        await automatic.MarkSourceAsync(source,ct);
        StructuredFormSuggestionCandidate? always=null;
        foreach(var candidate in configured.Value.Candidates.Where(candidate=>candidate.Mode=="Always"))
        {
            if(contact.Mode!=WhatsAppConversationMode.Manual && (candidate.CreatesAutomaticLoop
                || await reader.ExistsDeclinedAsync(contact.Id,candidate.Form.DefinitionId,ct)))continue;
            always=candidate;
            break;
        }
        if(always is not null && configured.Value.SuggestionSetId is {} setId)
        {
            WhatsAppAssistantRequest row;
            if(contact.Mode==WhatsAppConversationMode.Manual)
                row=WhatsAppAssistantRequest.CreateConfiguredManual(Guid.CreateVersion7(),contact.Id,source.Id,contact.UserId.Value,
                    recordId,setId,always.SuggestionId,culture.Value,contact.ModeRevision,contact.LinkRevision,proposals.NowUtc,source.ContentExpiresAtUtc);
            else
            {
                var captured=await access.CaptureAsync(contact,null,ct);if(captured.IsFailure)return Result.Success<WhatsAppAssistantWorkResponse?>(null);
                row=CreateAutomatic(contact,source,WhatsAppAssistantAccessPolicy.ForCapturedRecord(captured.Value.Context, recordId));
                if(!row.SetCapturedSuggestion(recordId,setId,always.SuggestionId,culture.Value,true,proposals.NowUtc))return WhatsAppAssistantErrors.StateConflict;
            }
            requests.Add(row);
            if (row.Kind == WhatsAppAssistantRequestKind.Automatic && !await AllowNewAutomaticAsync(contact, row, ct))
                return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            return Result.Success<WhatsAppAssistantWorkResponse?>(Work(row,source,false));
        }
        if(contact.Mode==WhatsAppConversationMode.Manual)return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        var context=await access.CaptureAsync(contact,null,ct);if(context.IsFailure)return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        var interpreted=CreateAutomatic(contact,source,WhatsAppAssistantAccessPolicy.ForCapturedRecord(context.Value.Context, recordId));
        if(!interpreted.SetCapturedSuggestion(recordId,null,null,culture.Value,false,proposals.NowUtc))return WhatsAppAssistantErrors.StateConflict;
        requests.Add(interpreted);
        if (!await AllowNewAutomaticAsync(contact, interpreted, ct)) return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        if(!configured.Value.Candidates.Any(candidate=>candidate.Mode=="Assistant"))
        {
            interpreted.FinishWithoutReply(false,proposals.NowUtc);return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        }
        return Result.Success<WhatsAppAssistantWorkResponse?>(Work(interpreted,source,true));
    }

    private async Task<bool> AllowNewAutomaticAsync(WhatsAppContact contact, WhatsAppAssistantRequest row, CancellationToken ct)
    {
        if (await automatic.HasOfferCapacityAsync(contact.Id, WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row)), ct)) return true;
        row.FinishWithoutReply(true, proposals.NowUtc);
        return false;
    }

    private WhatsAppAssistantRequest CreateAutomatic(WhatsAppContact contact,WhatsAppAssistantSourceRow source,AssistantChannelContext context)=>
        WhatsAppAssistantRequest.Create(Guid.CreateVersion7(),contact.Id,source.Id,context.ActorUserId,context.SubjectUserId!.Value,
            WhatsAppAssistantRequestKind.Automatic,contact.ModeRevision,contact.LinkRevision,JsonSerializer.Serialize(context,JsonSerializerOptions.Web),
            proposals.NowUtc,source.ContentExpiresAtUtc);

    private static bool MatchesSource(WhatsAppAssistantRequest row,WhatsAppAssistantSourceRow? source)=>source is not null
        && (MakesRequest(row,source) || row.SourceRecordId.HasValue && source.CapturedRecordId==row.SourceRecordId);

    // The latest source still makes this reference's request: the same message, with the same spreadsheet (IA, Stage 2), interpreted
    // now or, a spreadsheet without a request, as the answer the reference continues.
    private static bool MakesRequest(WhatsAppAssistantRequest row,WhatsAppAssistantSourceRow source)=>source.Id==row.SourceMessageId
        && source.Spreadsheet?.MessageId==row.AttachmentMessageId
        && (source.State==WhatsAppAssistantSourceState.Ready || source.State==WhatsAppAssistantSourceState.Alone && row.PreviousRequestId.HasValue);

    private static WhatsAppAssistantWorkResponse Work(WhatsAppAssistantRequest row,WhatsAppAssistantSourceRow source,bool submit)=>
        row.IsAlways?new(row.RequestId,null){ShouldSubmit=false}:new(row.RequestId,new(row.RequestId,
            WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row)),row.SourceRecordId is {} recordId
                ? "A form response was captured. Evaluate only its configured next-form suggestions. Source record: "+recordId.ToString("D")+"."
                : source.Body,source.OccurredAtUtc,row.ContentExpiresAtUtc)
            {PreviousRequestId=row.PreviousRequestId,Attachment=row.AttachmentMessageId is {} attachment && source.Spreadsheet is {} spreadsheet
                && spreadsheet.MessageId==attachment ? WhatsAppAssistantSourcePolicy.Attachment(spreadsheet) : null}){ShouldSubmit=submit};

    public Task<Result> CompleteConfiguredAsync(Guid contactId,Guid requestId,CancellationToken ct)=>
        OperationLog.RunAsync<Result>(logger,"CompleteConfiguredWhatsAppSuggestion",()=>CompleteConfiguredCoreAsync(contactId,requestId,ct));

    private async Task<Result> CompleteConfiguredCoreAsync(Guid contactId,Guid requestId,CancellationToken ct)
    {
        var original=await reader.FindAsync(contactId,requestId,ct);
        if(original is null || !original.IsAlways || original.State!=WhatsAppAssistantRequestState.Pending)return Result.Success();
        var view=await proposals.ReadConfiguredAsync(original,null,ct);
        Result<WhatsAppFormSendContext?>? prepared=null;
        if(original.Kind==WhatsAppAssistantRequestKind.Automatic && view.IsSuccess)
            prepared=await automatic.ResolveAsync(contactId,view.Value,ct);
        return await unitOfWork.ExecuteInTransactionAsync(async token=>
        {
            var contact=await conversations.GetForUpdateAsync(contactId,token);
            var row=await requests.GetByRequestAsync(requestId,token);
            if(contact is null || row?.ContactId!=contactId || !row.IsAlways || row.State!=WhatsAppAssistantRequestState.Pending)return Result.Success();
            if(view.IsFailure || view.Value.Prefill is null || await access.RevalidateAsync(contact,WhatsAppAssistantSuggestionPolicy.Reference(row),token) is not null
                || !MatchesSource(row,await reader.FindLatestSourceAsync(contactId,token)))
            {
                row.FinishWithoutReply(true,proposals.NowUtc);return Result.Success();
            }
            if(row.Kind==WhatsAppAssistantRequestKind.Manual)
                return row.Ready(null,view.Value.Prefill.Form.DefinitionId,proposals.NowUtc)?Result.Success():WhatsAppAssistantErrors.StateConflict;
            if(prepared is not {IsSuccess:true} context || (await automatic.AcceptAsync(contact,row,view.Value,context.Value,token)).IsFailure)
                row.FinishWithoutReply(true,proposals.NowUtc);
            return Result.Success();
        },CommitPolicy.OnSuccess,ct);
    }

    public Task<Result> CompleteAsync(Guid contactId, Guid requestId, Result<AssistantOperationResponse> outcome, CancellationToken ct) =>
        OperationLog.RunAsync<Result>(logger, "CompleteWhatsAppAssistant", async () =>
        {
            var result = await CompleteCoreAsync(contactId, requestId, outcome, ct);
            if (result.IsSuccess && await reader.FindAsync(contactId, requestId, ct) is { State: WhatsAppAssistantRequestState.Blocked } blocked)
                await proposals.CancelAsync(blocked, ct);
            return result;
        });

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
        if (outcome.IsSuccess && outcome.Value.Proposal?.Capability is {} capability && WhatsAppAssistantTerminalPolicy.Handles(capability)
            && await reader.FindSourceAsync(contactId,original.SourceMessageId,ct) is {} terminalSource)
            terminal=await automatic.PrepareTerminalAsync(original,terminalSource,outcome.Value,ct);
        // Casos del asistente: the agent's actions are checked against what is saved now, outside the contact/request transaction.
        Result<WhatsAppAssistantReplyPlan>? reply = null;
        if (outcome.IsSuccess && original.Kind == WhatsAppAssistantRequestKind.Automatic && selectedDefinition is null
            && WhatsAppAssistantReplyPolicy.Handles(outcome.Value))
            reply = await automatic.PrepareReplyAsync(original, outcome.Value, ct);
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
                var offeredQuestion = row.QuestionOutgoingId is {} questionId ? await conversations.GetOutgoingAsync(questionId, token) : null;
                if (offeredQuestion?.ContactId != contactId || offeredQuestion.Origin != WhatsAppMessageOrigin.Assistant
                    || offeredQuestion.Kind != WhatsAppOutgoingMessageKind.QuickReplies || !offeredQuestion.QuickReplies.Any(reply => reply.Id == choice.ReplyId))
                    return Result.Success();
                if (await access.RevalidateAsync(contact, WhatsAppAssistantSuggestionPolicy.Reference(row), token) is not null
                    || observed.IsFailure || observed.Value.Prefill is null)
                {
                    row.FinishWithoutReply(true, proposals.NowUtc);
                    return Result.Success();
                }
            }
            else if (await access.RevalidateAsync(contact, WhatsAppAssistantSuggestionPolicy.Reference(row), token) is not null)
            {
                row.FinishWithoutReply(true, proposals.NowUtc);
                return Result.Success();
            }
            else if (await reader.FindLatestSourceAsync(contactId, token) is not {} latest || !MakesRequest(row, latest))
            {
                // Its result came after a newer message replaced it: that message continues what this one continued.
                row.Replace(proposals.NowUtc);
                return Result.Success();
            }
            else if (observed.IsFailure)
            {
                row.FinishWithoutReply(true, proposals.NowUtc);
                return Result.Success();
            }
            if (reply is { } plan)
            {
                if (plan.IsFailure || (await automatic.AcceptReplyAsync(contact, row, plan.Value, token)).IsFailure)
                    row.FinishWithoutReply(true, proposals.NowUtc);
                return Result.Success();
            }
            if (terminal is {IsSuccess:true,Value: {} effect})
            {
                automatic.AcceptTerminal(contact,row,effect);
                return Result.Success();
            }
            var view = observed.Value;
            if(view.Suggestion is {} suggested && row.SourceRecordId is {} capturedRecord
                && !row.SetCapturedSuggestion(capturedRecord,suggested.OriginSuggestion.SuggestionSetId,suggested.OriginSuggestion.OriginSuggestionId,
                    WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row)).Culture,false,proposals.NowUtc))
            {row.FinishWithoutReply(true,proposals.NowUtc);return Result.Success();}
            if (view.Response.State == WhatsAppAssistantRequestState.Ready && view.Operation?.Proposal is {} proposal
                && (view.Prefill is not null || view.Clarification is not null || view.Response.TextProposal is not null))
            {
                if (row.Kind == WhatsAppAssistantRequestKind.Automatic)
                {
                    if (prepared is not { IsSuccess: true } context || (await automatic.AcceptAsync(contact, row, view, context.Value, token)).IsFailure)
                    {
                        row.FinishWithoutReply(true, proposals.NowUtc);
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
