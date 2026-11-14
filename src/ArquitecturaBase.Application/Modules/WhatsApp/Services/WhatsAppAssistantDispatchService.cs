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
            // A spreadsheet without a request is only that answer, and a text whose spreadsheet was rejected was already answered. A read
            // document without a request is also loaded (task 9 of the documents), with only the documents' capabilities.
            var alone=latest.State==WhatsAppAssistantSourceState.Alone;
            // VC1-A (task 29): a spreadsheet prepared to load its rows without a request is also loaded, with only the loading capabilities.
            var loads=WhatsAppAssistantSpreadsheetPolicy.Loads(latest.Spreadsheet);
            var interpretable=latest.State==WhatsAppAssistantSourceState.Ready || alone && (previous is not null || latest.Document is not null || loads);
            var captured=interpretable?await access.CaptureAsync(contact,null,ct):null;
            if(captured is not {IsSuccess:true} || latest.ContentExpiresAtUtc<=proposals.NowUtc)
            {
                await automatic.MarkSourceAsync(latest,ct);
                return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            }
            // A read document goes with the documents' capabilities: only them when it comes alone, or with the whole conversation.
            var context=latest.Document is not null
                ? alone && previous is null ? WhatsAppAssistantAccessPolicy.ForDocument(captured.Value.Context) : WhatsAppAssistantAccessPolicy.WithDocument(captured.Value.Context)
                : loads ? alone && previous is null ? WhatsAppAssistantAccessPolicy.ForSpreadsheet(captured.Value.Context) : WhatsAppAssistantAccessPolicy.WithSpreadsheet(captured.Value.Context)
                : captured.Value.Context;
            row=WhatsAppAssistantRequest.Create(Guid.CreateVersion7(),contactId,latest.Id,context.ActorUserId,
                context.SubjectUserId!.Value,WhatsAppAssistantRequestKind.Automatic,contact.ModeRevision,contact.LinkRevision,
                JsonSerializer.Serialize(context,JsonSerializerOptions.Web),proposals.NowUtc,latest.ContentExpiresAtUtc);
            if(previous is {} change) row.ContinueFrom(change);
            if((latest.Spreadsheet?.MessageId ?? latest.Document?.MessageId) is {} attachment) row.CarryAttachment(attachment);
            requests.Add(row);
        }
        if(row is null)return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        var revoked=await access.RevalidateAsync(contact,WhatsAppAssistantSuggestionPolicy.Reference(row),ct) is not null;
        if(revoked || !MatchesSource(row,latest))
        {
            supersede(WhatsAppAssistantSuggestionPolicy.Reference(row));
            // H5: it stopped being valid while it waited for the engine and its message is still the last one: a person answers.
            if(revoked && latest is not null && MakesRequest(row,latest) && WhatsAppAssistantReplyPolicy.WaitedInVain(contact,WhatsAppAssistantSuggestionPolicy.Reference(row)))
                await automatic.HandOffFailureAsync(contact,row,ct);
            // A newer message replaced it before it answered: the message that replaces it continues what it continued.
            else if(revoked) row.FinishWithoutReply(true,proposals.NowUtc); else row.Replace(proposals.NowUtc);
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

    // The latest source still makes this reference's request: the same message, with the same spreadsheet (IA, Stage 2) or read
    // document (task 9), interpreted now or, without a request, as the answer the reference continues or a document to load.
    private static bool MakesRequest(WhatsAppAssistantRequest row,WhatsAppAssistantSourceRow source)=>
        MakesRequest(row.SourceMessageId,row.AttachmentMessageId,row.PreviousRequestId.HasValue,source);

    private static bool MakesRequest(Guid sourceMessageId,Guid? attachmentMessageId,bool continues,WhatsAppAssistantSourceRow source)=>source.Id==sourceMessageId
        && (source.Spreadsheet?.MessageId ?? source.Document?.MessageId)==attachmentMessageId
        && (source.State==WhatsAppAssistantSourceState.Ready
            || source.State==WhatsAppAssistantSourceState.Alone && (continues || source.Document is not null
                || WhatsAppAssistantSpreadsheetPolicy.Loads(source.Spreadsheet)));

    private static WhatsAppAssistantWorkResponse Work(WhatsAppAssistantRequest row,WhatsAppAssistantSourceRow source,bool submit)
    {
        if(row.IsAlways) return new(row.RequestId,null){ShouldSubmit=false};
        var context=WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row));
        // A read document (task 9) or a spreadsheet to load (VC1-A, task 29) without a request is named by the server, under the
        // condition that seals its context to only load it: a channel turn always says something.
        var named=string.IsNullOrWhiteSpace(source.Body) && row.PreviousRequestId is null;
        var text=row.SourceRecordId is {} recordId
            ? "A form response was captured. Evaluate only its configured next-form suggestions. Source record: "+recordId.ToString("D")+"."
            : named && source.Document is {} alone ? WhatsAppAssistantDocumentPolicy.Text("DocumentAlone",context.Culture,alone.FileName)
            : named && source.Spreadsheet is {} sheet && WhatsAppAssistantSpreadsheetPolicy.Loads(sheet)
                ? WhatsAppAssistantDocumentPolicy.Text("SpreadsheetAlone",context.Culture,sheet.FileName) : source.Body;
        var attachment=row.AttachmentMessageId is not {} attached ? null
            : source.Spreadsheet is {} spreadsheet && spreadsheet.MessageId==attached ? WhatsAppAssistantSourcePolicy.Attachment(spreadsheet)
            : source.Document is {} document && document.MessageId==attached ? WhatsAppAssistantSourcePolicy.Attachment(document) : null;
        return new(row.RequestId,new(row.RequestId,context,text,source.OccurredAtUtc,row.ContentExpiresAtUtc)
            {PreviousRequestId=row.PreviousRequestId,Attachment=attachment}){ShouldSubmit=submit};
    }

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
        // Task 9 of the documents: a read document's proposal, or a document alone that closed without one, goes to the inbox.
        // VC1-A (task 29): and so do the rows of a spreadsheet; what the inbox did not register, or registered before, is not said.
        var document = outcome.IsSuccess && (outcome.Value.Proposal?.Capability is {} documentCapability
                && (WhatsAppAssistantDocumentPolicy.Handles(documentCapability) || WhatsAppAssistantSpreadsheetPolicy.Handles(documentCapability))
            || WhatsAppAssistantTerminalPolicy.HandlesDocumentWithoutProposal(original, outcome.Value));
        // VC1-A (task 29): over the day's quota a spreadsheet to load goes to the inbox unclassified and its person is told (rule B7).
        var quota = WhatsAppReceivedDocumentReadingPolicy.IsQuota(outcome) && original.Kind == WhatsAppAssistantRequestKind.Automatic && original.AttachmentMessageId is not null;
        // Review of task 29: the rows of a spreadsheet are registered only while this reference still makes the request; after a newer message
        // it is replaced below and that message answers them, also from what the inbox kept when it registered them in between.
        var current = !(quota || outcome.IsSuccess && WhatsAppAssistantTerminalPolicy.RegistersRows(original, outcome.Value))
            || await reader.FindLatestSourceAsync(contactId, ct) is {} newest
                && MakesRequest(original.SourceMessageId, original.AttachmentMessageId, original.PreviousRequestId.HasValue, newest);
        if (current && outcome.IsSuccess && (outcome.Value.Proposal?.Capability is {} capability && WhatsAppAssistantTerminalPolicy.Handles(capability) || document)
            && await reader.FindSourceAsync(contactId,original.SourceMessageId,ct) is {} terminalSource)
            terminal=await automatic.PrepareTerminalAsync(original,terminalSource,outcome.Value,ct);
        if (current && quota && await reader.FindSourceAsync(contactId, original.SourceMessageId, ct) is { Spreadsheet: {} quotaSheet } quotaSource
            && WhatsAppAssistantSpreadsheetPolicy.Loads(quotaSheet))
            terminal = await automatic.RegisterSpreadsheetOverQuotaAsync(original, quotaSource, ct);
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
        // H5: the engine did not answer the person's message; a spreadsheet over the day's quota was already told so.
        var failed = selectedDefinition is null && WhatsAppAssistantReplyPolicy.EngineFailed(original, outcome)
            && !(quota && terminal is {IsSuccess:true,Value:not null});
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
            else if (failed && WhatsAppAssistantAccessPolicy.InSameConversation(contact, WhatsAppAssistantSuggestionPolicy.Reference(row))
                && await reader.FindLatestSourceAsync(contactId, token) is {} unanswered && MakesRequest(row, unanswered))
            {
                // H5: in the conversation it was made in and while it is still the last message, also past the window.
                await automatic.HandOffFailureAsync(contact, row, token);
                return Result.Success();
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
            else if (observed.IsFailure && !(quota && terminal is {IsSuccess:true,Value:not null}))
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
                if (effect.Send is not null || effect.HandOff is not null || effect.List is not null || effect.Loads is not null)
                {
                    if (!await automatic.AcceptTerminalAsync(contact,row,effect,token)) row.FinishWithoutReply(true, proposals.NowUtc);
                }
                else automatic.AcceptTerminal(contact,row,effect);
                return Result.Success();
            }
            // What the inbox did not register, or a file already answered when it arrived, is not said.
            if (document && terminal is {IsSuccess:true,Value:null})
            {
                row.FinishWithoutReply(false, proposals.NowUtc);
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
