using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppAssistantAutomaticPolicy(WhatsAppAssistantAcceptancePolicy acceptance,
    IWhatsAppAssistantReader reader,TimeProvider clock,IWhatsAppConversationRepository conversations,IWhatsAppMessageRepository messages,WhatsAppAssistantTerminalPolicy terminals,
    WhatsAppAssistantOfferCapacityPolicy capacity,WhatsAppAssistantReplyPolicy replies)
{
    public async Task<Result<WhatsAppAssistantTerminalResponse?>> PrepareTerminalAsync(WhatsAppAssistantReferenceRow reference,WhatsAppAssistantSourceRow source,AssistantOperationResponse operation,CancellationToken ct)
    {
        var nowUtc=clock.GetUtcNow().UtcDateTime;
        if(reference.State!=WhatsAppAssistantRequestState.Pending || reference.ContentExpiresAtUtc<=nowUtc
            || source.ContentExpiresAtUtc<=nowUtc || !await reader.ExistsCurrentContextAsync(reference,nowUtc,ct))
            return WhatsAppAssistantErrors.StateConflict;
        return await terminals.ApplyAsync(reference,source,operation,ct);
    }

    // Casos del asistente: the actions of a reply are checked outside the unit of work and queued inside it, in order.
    public Task<Result<WhatsAppAssistantReplyPlan>> PrepareReplyAsync(WhatsAppAssistantReferenceRow reference,AssistantOperationResponse operation,CancellationToken ct)=>
        replies.PrepareAsync(reference,operation,ct);
    public Task<Result> AcceptReplyAsync(WhatsAppContact contact,WhatsAppAssistantRequest row,WhatsAppAssistantReplyPlan plan,CancellationToken ct)=>
        replies.ApplyAsync(contact,row,plan,ct);
    public Task<bool> ApplyCaseTapAsync(WhatsAppContact contact,WhatsAppAssistantChoiceRow tap,CancellationToken ct)=>replies.ApplyTapAsync(contact,tap,ct);
    public Task<bool> WaitsForBurstAsync(Guid contactId,DateTime latestUtc,CancellationToken ct)=>replies.WaitsForBurstAsync(contactId,latestUtc,ct);

    public bool AcceptTerminal(WhatsAppContact contact,WhatsAppAssistantRequest row,WhatsAppAssistantTerminalResponse terminal)
    {
        var nowUtc=clock.GetUtcNow().UtcDateTime;
        if(row.Kind==WhatsAppAssistantRequestKind.Manual) return row.FinishWithoutReply(false,nowUtc);
        if(terminal.Body is null) return false;
        // A question about what to build waits for the answer: the next message continues this operation (IA3-A).
        if(terminal.AwaitsAnswer && !row.AwaitAnswer()) return false;
        // An authoring summary goes with its two buttons, a document question with its buttons or its list (task 9); any other reply
        // is a fixed text.
        var outgoing=terminal is {List:{} rows,ListButton:{} button}
            ? WhatsAppOutgoingMessage.CreateAssistantList(row.SubjectUserId,contact.Id,row.RequestId,terminal.Body,button,rows,nowUtc,
                contact.LinkRevision,contact.WaId!,row.ModeRevision)
            : terminal.Buttons is {} buttons
            ? WhatsAppOutgoingMessage.CreateAssistantQuickReplies(row.SubjectUserId,contact.Id,row.RequestId,terminal.Body,buttons,nowUtc,
                contact.LinkRevision,contact.WaId!,row.ModeRevision)
            : WhatsAppOutgoingMessage.CreateAssistantText(row.SubjectUserId,contact.Id,row.RequestId,terminal.Body,nowUtc,
                contact.LinkRevision,contact.WaId!,row.ModeRevision);
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.RequestId.ToString("N")+terminal.ProposalId.ToString("N"))));
        if(!row.Ready(terminal.ProposalId,null,nowUtc) || !row.Accept(outgoing.Id,fingerprint,null,nowUtc)) return false;
        conversations.Add(outgoing);
        return true;
    }

    /// <summary>
    /// Task 9 of the documents: the reply to a read document, in the caller's transaction. The prefilled form goes out with its text, a
    /// form that cannot be sent hands off with its text, and the rest goes as <see cref="AcceptTerminal"/> queues it. False when nothing
    /// could be queued.
    /// </summary>
    public async Task<bool> AcceptTerminalAsync(WhatsAppContact contact,WhatsAppAssistantRequest row,WhatsAppAssistantTerminalResponse terminal,CancellationToken ct)
    {
        if(row.Kind==WhatsAppAssistantRequestKind.Automatic && terminal.Send is not null)
            return (await acceptance.AcceptDocumentAsync(contact,row,terminal,ct)).IsSuccess;
        if(row.Kind==WhatsAppAssistantRequestKind.Automatic && terminal is {HandOff:{} handoff,Body:{} message})
        {
            var nowUtc=clock.GetUtcNow().UtcDateTime;
            var outgoing=await acceptance.HandOffAsync(contact,row,handoff,message,ct);
            if(outgoing is null) return row.FinishWithoutReply(false,nowUtc);
            var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.RequestId.ToString("N")+terminal.ProposalId.ToString("N"))));
            return row.Ready(terminal.ProposalId,null,nowUtc) && row.Accept(outgoing.Id,fingerprint,null,nowUtc);
        }
        return AcceptTerminal(contact,row,terminal);
    }

    public async Task<Result<WhatsAppFormSendContext?>> ResolveAsync(Guid contactId,WhatsAppAssistantSuggestionView view,CancellationToken ct)
    {
        if(view.Prefill is null && view.Clarification is not null) return Result.Success<WhatsAppFormSendContext?>(null);
        var resolved=await acceptance.ResolveAsync(contactId,view,null,ct);
        return resolved.IsSuccess ? Result.Success<WhatsAppFormSendContext?>(resolved.Value) : resolved.Error;
    }

    // Called under the same contact/request lock as the actual enqueue, after discovery has completed.
    public async Task<Result> AcceptAsync(WhatsAppContact contact,WhatsAppAssistantRequest row,
        WhatsAppAssistantSuggestionView view,WhatsAppFormSendContext? context,CancellationToken ct)
    {
        if(await reader.ExistsUnresolvedOfferAsync(contact.Id,row.RequestId,ct) || view.Suggestion?.CreatesAutomaticLoop==true) return WhatsAppAssistantErrors.StateConflict;
        var definitions=view.Prefill is {} prefill ? new[]{prefill.Form.DefinitionId}
            : view.Clarification is {} clarification ? clarification.Choices.Select(choice=>choice.DefinitionId).ToArray() : Array.Empty<Guid>();
        if(definitions.Length==0) return WhatsAppAssistantErrors.StateConflict;
        foreach(var definition in definitions)
            if(await reader.ExistsDeclinedAsync(contact.Id,definition,ct)) return WhatsAppAssistantErrors.StateConflict;
        var channel = WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row));
        if (!await HasOfferCapacityAsync(contact.Id, channel, ct, row.RequestId)) return WhatsAppAssistantErrors.StateConflict;
        if(view.Prefill is null && view.Clarification is {} question)
        {
            if(await conversations.ExistsUnresolvedOutgoingAsync(contact.Id,ct)) return WhatsAppChatErrors.SendPending;
            if(view.Operation?.Proposal is not {} proposal || question.Choices.Count is <2 or >3
                || question.Choices.Any(choice=>string.IsNullOrWhiteSpace(choice.Label) || choice.Label.Any(char.IsControl)))
                return WhatsAppAssistantErrors.StateConflict;
            var nowUtc=clock.GetUtcNow().UtcDateTime;
            var titles=question.Choices.Select(choice=>ShortTitle(choice.Label,20)).ToArray();
            if(titles.Distinct(StringComparer.Ordinal).Count()!=titles.Length)
                titles=question.Choices.Select((choice,index)=>(index+1).ToString(CultureInfo.InvariantCulture)+". "+ShortTitle(choice.Label,17)).ToArray();
            var body=titles.Where((title,index)=>title!=question.Choices[index].Label).Any()
                ? question.Question+"\n"+string.Join("\n",question.Choices.Select((choice,index)=>(index+1).ToString(CultureInfo.InvariantCulture)+". "+choice.Label))
                : question.Question;
            var replies=question.Choices.Select((choice,index)=>new WhatsAppOutgoingQuickReply(
                WhatsAppAssistantReplyCodec.Encode(row.RequestId,choice.DefinitionId,choice.VersionId),titles[index],choice.DefinitionRevision)).ToArray();
            var outgoing=WhatsAppOutgoingMessage.CreateAssistantQuickReplies(row.SubjectUserId,contact.Id,row.RequestId,body,
                replies,nowUtc,contact.LinkRevision,contact.WaId!,row.ModeRevision);
            if(!row.Ready(proposal.Id,null,nowUtc) || !row.AttachQuestion(outgoing.Id,nowUtc)) return WhatsAppAssistantErrors.StateConflict;
            conversations.Add(outgoing);
            return Result.Success();
        }
        if(context is null) return WhatsAppAssistantErrors.StateConflict;
        var accepted=await acceptance.AcceptAutomaticAsync(contact,row,view,context,ct);
        return accepted.IsSuccess?Result.Success():accepted.Error;
    }
    public Task<bool> HasOfferCapacityAsync(Guid contactId, AssistantChannelContext channel, CancellationToken ct, Guid? excludingRequestId = null) =>
        capacity.HasCapacityAsync(contactId, channel, ct, excludingRequestId);

    private static string ShortTitle(string title,int maximumLength)
    {
        if(title.Length<=maximumLength)return title;
        var end=StringInfo.ParseCombiningCharacters(title).Skip(1).Append(title.Length).Where(end=>end<=maximumLength-1).LastOrDefault();
        return title[..end]+"…";
    }

    public async Task<bool> RecordDeclineAsync(WhatsAppAssistantRequest row,WhatsAppAssistantSuggestionView view,CancellationToken ct)
    {
        if(view.DeclinedDefinitionId is not {} definitionId || view.Operation?.Proposal?.Payload is not WhatsAppAssistantDeclinePayload decline)
            return false;
        var offers=await reader.ListOffersAsync(row.SubjectUserId,ct);
        return offers.Any(offer=>offer.RequestId==decline.RequestId && offer.DefinitionId==definitionId)
            && row.RecordDecline(definitionId,clock.GetUtcNow().UtcDateTime);
    }
    public async Task MarkSourceAsync(WhatsAppAssistantSourceRow source,CancellationToken ct)
    {
        if(source.WaMessageId is not {} waMessageId)return;
        var message=await messages.GetInboundAsync(waMessageId,ct);
        if(message?.Id==source.Id && message.ContactId==source.ContactId)message.MarkAssistantProcessed(clock.GetUtcNow().UtcDateTime);
    }
    public async Task MarkChoiceAsync(WhatsAppAssistantChoiceRow choice,Guid contactId,CancellationToken ct)
    {
        var message=await messages.GetInboundAsync(choice.WaMessageId,ct);
        if(message?.Id==choice.Id && message.ContactId==contactId) message.MarkAssistantProcessed(clock.GetUtcNow().UtcDateTime);
    }
}
