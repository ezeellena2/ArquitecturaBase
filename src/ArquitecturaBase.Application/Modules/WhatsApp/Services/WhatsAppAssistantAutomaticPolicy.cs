using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppAssistantAutomaticPolicy(WhatsAppAssistantAcceptancePolicy acceptance,
    IWhatsAppAssistantReader reader,TimeProvider clock,IWhatsAppConversationRepository conversations,IWhatsAppMessageRepository messages,WhatsAppAssistantTerminalPolicy terminals)
{
    public async Task<Result<WhatsAppAssistantTerminalResponse?>> PrepareTerminalAsync(WhatsAppAssistantReferenceRow reference,WhatsAppAssistantSourceRow source,AssistantOperationResponse operation,CancellationToken ct)
    {
        var nowUtc=clock.GetUtcNow().UtcDateTime;
        if(reference.State!=WhatsAppAssistantRequestState.Pending || reference.ContentExpiresAtUtc<=nowUtc
            || source.ContentExpiresAtUtc<=nowUtc || !await reader.ExistsCurrentContextAsync(reference,nowUtc,ct))
            return WhatsAppAssistantErrors.StateConflict;
        return await terminals.ApplyAsync(reference,source,operation,ct);
    }

    public bool AcceptTerminal(WhatsAppContact contact,WhatsAppAssistantRequest row,WhatsAppAssistantTerminalResponse terminal)
    {
        var nowUtc=clock.GetUtcNow().UtcDateTime;
        if(row.Kind==WhatsAppAssistantRequestKind.Manual) return row.FinishWithoutReply(false,nowUtc);
        if(terminal.Body is null) return false;
        var outgoing=WhatsAppOutgoingMessage.CreateAssistantText(row.SubjectUserId,contact.Id,row.RequestId,terminal.Body,nowUtc,
            contact.LinkRevision,contact.WaId!,row.ModeRevision);
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.RequestId.ToString("N")+terminal.ProposalId.ToString("N"))));
        if(!row.Ready(terminal.ProposalId,null,nowUtc) || !row.Accept(outgoing.Id,fingerprint,null,nowUtc)) return false;
        conversations.Add(outgoing);
        return true;
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
        if(await reader.ExistsUnresolvedOfferAsync(contact.Id,row.RequestId,ct)) return WhatsAppAssistantErrors.StateConflict;
        var definitions=view.Prefill is {} prefill ? new[]{prefill.Form.DefinitionId}
            : view.Clarification is {} clarification ? clarification.Choices.Select(choice=>choice.DefinitionId).ToArray() : Array.Empty<Guid>();
        if(definitions.Length==0) return WhatsAppAssistantErrors.StateConflict;
        foreach(var definition in definitions)
            if(await reader.ExistsDeclinedAsync(contact.Id,definition,ct)) return WhatsAppAssistantErrors.StateConflict;
        var channel = WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(channel.TimeZoneId);
        var localDay = TimeZoneInfo.ConvertTime(clock.GetUtcNow(),zone).Date;
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(localDay,zone);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(localDay.AddDays(1),zone);
        if (await reader.CountOffersAsync(contact.Id,fromUtc,toUtc,ct,row.RequestId) >= 2)
            return WhatsAppAssistantErrors.StateConflict;
        if(view.Prefill is null && view.Clarification is {} question)
        {
            if(await conversations.ExistsUnresolvedOutgoingAsync(contact.Id,ct)) return WhatsAppChatErrors.SendPending;
            if(view.Operation?.Proposal is not {} proposal || question.Choices.Count is <2 or >3
                || question.Choices.Any(choice=>string.IsNullOrWhiteSpace(choice.Label) || choice.Label.Length>20 || choice.Label.Any(char.IsControl)))
                return WhatsAppAssistantErrors.StateConflict;
            var nowUtc=clock.GetUtcNow().UtcDateTime;
            var replies=question.Choices.Select(choice=>new WhatsAppOutgoingQuickReply(
                WhatsAppAssistantReplyCodec.Encode(row.RequestId,choice.DefinitionId,choice.VersionId),choice.Label,choice.DefinitionRevision)).ToArray();
            var outgoing=WhatsAppOutgoingMessage.CreateAssistantQuickReplies(row.SubjectUserId,contact.Id,row.RequestId,question.Question,
                replies,nowUtc,contact.LinkRevision,contact.WaId!,row.ModeRevision);
            if(!row.Ready(proposal.Id,null,nowUtc) || !row.AttachQuestion(outgoing.Id,nowUtc)) return WhatsAppAssistantErrors.StateConflict;
            conversations.Add(outgoing);
            return Result.Success();
        }
        if(context is null) return WhatsAppAssistantErrors.StateConflict;
        var accepted=await acceptance.AcceptAutomaticAsync(contact,row,view,context,ct);
        return accepted.IsSuccess?Result.Success():accepted.Error;
    }
    public async Task<bool> RecordDeclineAsync(WhatsAppAssistantRequest row,WhatsAppAssistantSuggestionView view,CancellationToken ct)
    {
        if(view.DeclinedDefinitionId is not {} definitionId || view.Operation?.Proposal?.Payload is not WhatsAppAssistantDeclinePayload decline)
            return false;
        var offers=await reader.ListOffersAsync(row.SubjectUserId,ct);
        return offers.Any(offer=>offer.RequestId==decline.RequestId && offer.DefinitionId==definitionId)
            && row.RecordDecline(definitionId,clock.GetUtcNow().UtcDateTime);
    }
    public async Task MarkChoiceAsync(WhatsAppAssistantChoiceRow choice,Guid contactId,CancellationToken ct)
    {
        var message=await messages.GetInboundAsync(choice.WaMessageId,ct);
        if(message?.Id==choice.Id && message.ContactId==contactId) message.MarkAssistantProcessed(clock.GetUtcNow().UtcDateTime);
    }
}
