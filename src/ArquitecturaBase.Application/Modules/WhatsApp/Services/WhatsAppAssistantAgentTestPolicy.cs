using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// Probar (AS3): the context of a test, rebuilt the same way to start it and to read it, and what a conversation would get from its
/// proposals, checked like a conversation checks them (without spending its daily limit).
/// </summary>
internal sealed class WhatsAppAssistantAgentTestPolicy(IWhatsAppFormRecipientReader recipients, ISystemSettingsReader settings, IStructuredFormSource forms)
{
    public const string ChannelId = "whatsapp-assistant-test";

    private static readonly AssistantCapabilityReference[] Capabilities =
    [
        new(WhatsAppAssistantAgentCapabilitySource.AgentId, 1), new(WhatsAppAssistantAgentCapabilitySource.KnowledgeId, 1),
        new(WhatsAppAssistantAgentCapabilitySource.HistoryId, 1), new("forms.assistant-candidates", 1), new("forms.propose-registration", 1),
        new("forms.clarify-registration", 1), new(WhatsAppAssistantAgentCapabilitySource.ReplyId, 1), new(WhatsAppAssistantAgentCapabilitySource.DraftId, 1),
    ];

    /// <summary>The tester is the Actor and the chosen account the Subject; the conversation is that account's chat.</summary>
    public async Task<Result<AssistantChannelContext>> ContextAsync(Guid actorUserId, Guid contactId, CancellationToken ct)
    {
        var recipient = await recipients.FindAsync(contactId, ct);
        if (recipient?.UserId is not { } subject || !recipient.IsActive || recipient.Culture is not ("es" or "en")) return WhatsAppChatErrors.NotFound;
        var presentation = await settings.FindPresentationAsync(ct);
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(presentation.DefaultTimeZoneId, out _)) return WhatsAppAssistantErrors.Unavailable;
        return new AssistantChannelContext(ChannelId, actorUserId, subject, recipient.Culture, presentation.DefaultTimeZoneId,
            new([WhatsAppPermissions.ConfigureAssistant], []), Capabilities, [new("assistant_proposal", 1)])
        { InteractionMode = AssistantInteractionMode.Assistant, ChannelConversationId = contactId };
    }

    /// <summary>
    /// The actions in their order: the agent's with the texts saved now (or the handoff that replaces what does not pass) and the
    /// forms with what they would fill. A proposal that no longer reads is left out.
    /// </summary>
    public async Task<IReadOnlyList<WhatsAppAssistantAgentTestAction>> ActionsAsync(AssistantOperationResponse operation, WhatsAppAssistantAgentRow agent,
        AssistantChannelContext context, CancellationToken ct)
    {
        var proposals = WhatsAppAssistantReplyPolicy.Proposals(operation);
        if (proposals.Count == 0) return [];
        var own = proposals.Where(WhatsAppAssistantReplyPolicy.IsAgent).ToArray();
        var (steps, failure) = WhatsAppAssistantReplyPolicy.Check(own, agent, context.Culture);
        if (failure is null && own.Length < proposals.Count
            && steps.Any(step => step.Record.Kind is WhatsAppAssistantActionKind.Handoff or WhatsAppAssistantActionKind.NoReply)) failure = "replyNotAllowed";
        if (failure is not null) return [.. WhatsAppAssistantReplyPolicy.HandOff(proposals[0], agent, context.Culture, failure).Steps.Select(Action)];
        var actions = new List<WhatsAppAssistantAgentTestAction>();
        var next = 0;
        foreach (var proposal in proposals)
        {
            if (WhatsAppAssistantReplyPolicy.IsAgent(proposal)) actions.Add(Action(steps[next++]));
            else if (await FormAsync(proposal, context, ct) is { } form) actions.Add(form);
        }

        return actions;
    }

    private async Task<WhatsAppAssistantAgentTestAction?> FormAsync(AssistantObservedProposal proposal, AssistantChannelContext context, CancellationToken ct)
    {
        var subject = context.SubjectUserId!.Value;
        if (proposal.Capability is { Id: "forms.propose-registration", Version: 1 })
        {
            var prefill = await forms.ValidatePrefillAsync(new(subject, context.Culture, proposal), ct);
            if (prefill.IsFailure) return null;
            var name = prefill.Value.Form.Name.GetValueOrDefault(prefill.Value.Culture) ?? prefill.Value.Form.Name.Values.FirstOrDefault() ?? "";
            return new(WhatsAppAssistantActionKind.SendForm, WhatsAppAssistantCaseSource.Form, name, prefill.Value.Reason, [], null, null,
                new(name, [.. prefill.Value.Fields.Select(field => new WhatsAppAssistantAgentTestField(field.Label, field.DisplayValue))]), null, null);
        }

        if (proposal.Capability is not { Id: "forms.clarify-registration", Version: 1 }) return null;
        var clarification = await forms.ValidateClarificationAsync(new(subject, context.Culture, proposal), ct);
        return clarification.IsFailure ? null : new(WhatsAppAssistantActionKind.AskButtons, WhatsAppAssistantCaseSource.Form, null, "", [],
            clarification.Value.Question, [.. clarification.Value.Choices.Select(choice => choice.Label)], null, null, null);
    }

    private static WhatsAppAssistantAgentTestAction Action(WhatsAppAssistantReplyStep step) => new(step.Record.Kind, step.Record.CaseSource,
        step.Record.CaseTitle, step.Record.Reason, step.Record.Used, step.Text, step.Buttons, null, step.Record.HandoffReasonCode, step.Record.HandoffReason);
}
