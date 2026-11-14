using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>One action of a reply, ready to queue: what goes out (a saved text, a checked draft, a question with its buttons, the
/// handoff message) and its «Por qué respondió esto».</summary>
internal sealed record WhatsAppAssistantReplyStep(Guid ProposalId, WhatsAppAssistantReplyRecord Record, string? Text,
    IReadOnlyList<string>? Buttons);

/// <summary>The actions of a reply in their order, or the handoff that replaces them.</summary>
internal sealed record WhatsAppAssistantReplyPlan(IReadOnlyList<WhatsAppAssistantReplyStep> Steps);

/// <summary>
/// Casos del asistente (ADR 0021, point 5): the actions of an automatic reply are checked before they are queued. Each one follows
/// a case that still exists, is active and allowed, with the text saved now; a draft passes the length, the language and what the
/// business loaded; a handoff or «No responder» goes alone; and the replies of a conversation have their own daily limit. A reply
/// that does not pass sends nothing of its own and hands off with the reason. It prepares outside the unit of work and queues
/// inside the caller's, under its locks of the contact and the reference; it never saves.
/// </summary>
internal sealed class WhatsAppAssistantReplyPolicy(IWhatsAppAssistantAgentReader agents, IWhatsAppAssistantHistoryReader history,
    WhatsAppAssistantHandoffPolicy handoffs, IWhatsAppConversationRepository conversations, IWhatsAppAssistantRepository requests,
    IWhatsAppMessageRepository messages, TimeProvider clock)
{
    private static readonly AssistantCapabilityReference Reply = new(WhatsAppAssistantAgentCapabilitySource.ReplyId, 1);
    private static readonly AssistantCapabilityReference Draft = new(WhatsAppAssistantAgentCapabilitySource.DraftId, 1);
    private static readonly AssistantCapabilityReference Knowledge = new(WhatsAppAssistantAgentCapabilitySource.KnowledgeId, 1);

    /// <summary>Ráfagas: how long the person has to stay quiet before the turn, and the most a burst waits since its first message.</summary>
    public static readonly TimeSpan BurstQuiet = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan BurstLimit = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Ráfagas (Casos del asistente): with an agent, the turn waits while the person is still writing, 4 s without a new message and
    /// at most 15 s since the first one of the burst, so it answers the whole burst (the history brings it). Without an agent the
    /// assistant answers as before.
    /// </summary>
    public async Task<bool> WaitsForBurstAsync(Guid contactId, DateTime latestUtc, CancellationToken ct)
    {
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        if (nowUtc - latestUtc >= BurstQuiet || await agents.FindMainAsync(ct) is null) return false;
        var startUtc = await history.FindBurstStartAsync(contactId, ct) ?? latestUtc;
        return nowUtc - (startUtc < latestUtc ? startUtc : latestUtc) < BurstLimit;
    }

    /// <summary>
    /// Whether the operation answers with the agent's proposals (a text, buttons, a handoff, «No responder» or a draft). Several
    /// proposals are a reply too: a form goes alone, so a reply that mixes one with others does not pass and hands off.
    /// </summary>
    public static bool Handles(AssistantOperationResponse operation) =>
        operation.State == "completed" && (Proposals(operation).Any(IsAgent) || Proposals(operation).Count > 1);

    public static bool IsAgent(AssistantObservedProposal proposal) => proposal.Capability == Reply || proposal.Capability == Draft;

    public static IReadOnlyList<AssistantObservedProposal> Proposals(AssistantOperationResponse operation) =>
        operation.Proposals.Count != 0 ? operation.Proposals : operation.Proposal is { } proposal ? [proposal] : [];

    /// <summary>
    /// The actions of the agent's proposals, checked against the agent as it is saved now, in their order; or the code of what did
    /// not pass. The daily limit is the caller's: a conversation counts it, Probar does not spend it.
    /// </summary>
    public static (IReadOnlyList<WhatsAppAssistantReplyStep> Steps, string? Failure) Check(IReadOnlyList<AssistantObservedProposal> proposals,
        WhatsAppAssistantAgentRow agent, string culture)
    {
        var steps = new List<WhatsAppAssistantReplyStep>();
        string? failure = null;
        foreach (var (proposal, index) in proposals.Select((proposal, index) => (proposal, index)))
        {
            var step = proposal.Payload switch
            {
                WhatsAppAssistantReplyPayload reply when proposal.Capability == Reply => FromReply(proposal.Id, reply, agent, culture),
                WhatsAppAssistantDraftPayload draft when proposal.Capability == Draft => FromDraft(proposal, draft, agent, culture, out failure),
                _ => null,
            };
            if (step is null)
            {
                failure ??= "replyNotAllowed";
                break;
            }

            steps.Add(step with { Record = step.Record with { Ordinal = index + 1, Count = proposals.Count } });
        }

        // A handoff or «No responder» is the whole reply.
        if (failure is null && steps.Count > 1 && steps.Any(step => step.Record.Kind is WhatsAppAssistantActionKind.Handoff or WhatsAppAssistantActionKind.NoReply))
            failure = "replyNotAllowed";
        return (steps, failure);
    }

    /// <summary>What did not pass does not go out: the conversation goes to a person, with the profile's message.</summary>
    public static WhatsAppAssistantReplyPlan HandOff(AssistantObservedProposal first, WhatsAppAssistantAgentRow agent, string culture, string failure)
    {
        var (source, caseId, title) = First(first.Payload);
        return new([new(first.Id, new(WhatsAppAssistantActionKind.Handoff, source, caseId, title, "", [], 1, 1, failure),
            agent.Profile.HandoffMessage.For(culture, agent.OriginalCulture), null)]);
    }

    /// <summary>The code of a handoff because the engine did not answer (H5): «El asistente no pudo responder».</summary>
    public const string AssistantFailedCode = "assistantFailed";

    /// <summary>
    /// H5: whether a person answers because the engine did not: it failed, could not tell or expired, or it refused the request for
    /// good. What is pending or can be retried does not, nor a suggestion after a capture, which nobody asked. A request that stops
    /// being valid while it waits (the 24-hour window closed) hands off when the processor finds it (<see cref="WaitedInVain"/>).
    /// </summary>
    public static bool EngineFailed(WhatsAppAssistantReferenceRow reference, Result<AssistantOperationResponse> outcome) =>
        reference is { Kind: WhatsAppAssistantRequestKind.Automatic, SourceRecordId: null }
        && (outcome.IsFailure && outcome.Error.Type != ErrorType.NotFound && !WhatsAppReceivedDocumentReadingPolicy.IsRetryable(outcome.Error)
            || outcome.IsSuccess && outcome.Value.State is "failed" or "expired" or "uncertain");

    /// <summary>
    /// H5: a pending request of the person's message that stopped being valid while it waited for the engine (the 24-hour window
    /// closed, or the person's account changed) in the same conversation, which it still opens: a person answers.
    /// </summary>
    public static bool WaitedInVain(WhatsAppContact contact, WhatsAppAssistantReferenceRow reference) =>
        reference is { Kind: WhatsAppAssistantRequestKind.Automatic, SourceRecordId: null, State: WhatsAppAssistantRequestState.Pending }
        && WhatsAppAssistantAccessPolicy.InSameConversation(contact, reference);

    /// <summary>
    /// H5: the conversation goes to a person with the profile's message, as a reply that does not pass, in the caller's unit of work
    /// and under its locks of the contact and the reference. The reference stays Blocked; the message carries why, or the reference
    /// keeps it for the chat when nothing goes out (without an agent, its message or the 24-hour window).
    /// </summary>
    public async Task HandOffFailureAsync(WhatsAppContact contact, WhatsAppAssistantRequest row, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(row);
        var agent = await agents.FindMainAsync(ct);
        var culture = WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(row)).Culture;
        var why = WhatsAppAssistantReplyRecordCodec.Serialize(new(WhatsAppAssistantActionKind.Handoff, WhatsAppAssistantCaseSource.Fallback, null, null,
            "", [], 1, 1, AssistantFailedCode));
        var outgoing = await handoffs.HandOffAsync(contact, row.SubjectUserId, row.RequestId, AssistantFailedCode, null,
            agent is null ? null : agent.Profile.HandoffMessage.For(culture, agent.OriginalCulture), ct);
        if (outgoing is not null) outgoing.ExplainAssistantReply(why);
        else if (!row.RecordOutcome(why)) throw new InvalidOperationException("A failed request could not record why it handed off.");
        if (!row.FinishWithoutReply(true, clock.GetUtcNow().UtcDateTime)) throw new InvalidOperationException("A failed request could not finish.");
    }

    public static int Replies(IReadOnlyList<WhatsAppAssistantReplyStep> steps) => steps.Count(step => step.Record.Kind
        is WhatsAppAssistantActionKind.ExactText or WhatsAppAssistantActionKind.DraftedReply or WhatsAppAssistantActionKind.AskButtons);

    public async Task<Result<WhatsAppAssistantReplyPlan>> PrepareAsync(WhatsAppAssistantReferenceRow reference, AssistantOperationResponse operation,
        CancellationToken ct)
    {
        var proposals = Proposals(operation);
        if (reference.Kind != WhatsAppAssistantRequestKind.Automatic || operation.RequestId != reference.RequestId || operation.Provenance is null
            || proposals.Count is 0 or > WhatsAppAssistantRequest.MaxActions) return WhatsAppAssistantErrors.InvalidProposal;
        if (await agents.FindMainAsync(ct) is not { } agent) return WhatsAppAssistantErrors.Unavailable;
        var context = WhatsAppAssistantSuggestionPolicy.Context(reference);
        // A form or a question about which form goes alone: mixed with other actions, nothing of the reply goes out and it hands off.
        if (proposals.Any(proposal => !IsAgent(proposal))) return HandOff(proposals.FirstOrDefault(IsAgent) ?? proposals[0], agent, context.Culture, "replyNotAllowed");
        var (steps, failure) = Check(proposals, agent, context.Culture);
        var replies = Replies(steps);
        if (failure is null && replies > 0)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(context.TimeZoneId);
            var dayStartUtc = WhatsAppBusinessDay.StartUtc(WhatsAppBusinessDay.LocalDay(clock.GetUtcNow(), zone), zone);
            if (await history.CountRepliesAsync(reference.ContactId, dayStartUtc, ct) + replies > agent.Limits.DailyReplyLimit) failure = "dailyReplyLimit";
        }

        return failure is null ? new WhatsAppAssistantReplyPlan(steps) : HandOff(proposals[0], agent, context.Culture, failure);
    }

    public async Task<Result> ApplyAsync(WhatsAppContact contact, WhatsAppAssistantRequest first, WhatsAppAssistantReplyPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(plan);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        // Everything is checked before anything changes: a reply goes out whole or not at all.
        if (first.Kind != WhatsAppAssistantRequestKind.Automatic || first.ActionOrdinal != 0 || first.State != WhatsAppAssistantRequestState.Pending
            || nowUtc >= first.ContentExpiresAtUtc || first.ContactId != contact.Id || contact.WaId is not { } waId
            || plan.Steps.Count is 0 or > WhatsAppAssistantRequest.MaxActions
            || plan.Steps.Any(step => step.Record.Kind switch
            {
                WhatsAppAssistantActionKind.ExactText or WhatsAppAssistantActionKind.DraftedReply => string.IsNullOrWhiteSpace(step.Text),
                WhatsAppAssistantActionKind.AskButtons => string.IsNullOrWhiteSpace(step.Text) || step.Buttons is not { Count: >= 1 and <= 3 },
                WhatsAppAssistantActionKind.Handoff or WhatsAppAssistantActionKind.NoReply => plan.Steps.Count > 1,
                _ => true,
            }))
            return WhatsAppAssistantErrors.StateConflict;
        for (var index = 0; index < plan.Steps.Count; index++)
        {
            var step = plan.Steps[index];
            var row = first;
            if (index > 0)
            {
                row = WhatsAppAssistantRequest.CreateAction(first, Guid.CreateVersion7(), index, nowUtc);
                requests.Add(row);
            }

            var record = WhatsAppAssistantReplyRecordCodec.Serialize(step.Record);
            // H6: the queue sends by RequestedAtUtc and then by Id, a Guid v7 that is random within a millisecond: each action adds its
            // place in milliseconds, as «Seguir sin ubicación» does, so a reply goes out in the model's order.
            var requestedAtUtc = nowUtc.AddMilliseconds(index);
            WhatsAppOutgoingMessage? outgoing = step.Record.Kind switch
            {
                WhatsAppAssistantActionKind.ExactText or WhatsAppAssistantActionKind.DraftedReply => WhatsAppOutgoingMessage.CreateAssistantText(
                    row.SubjectUserId, contact.Id, row.RequestId, step.Text!, requestedAtUtc, contact.LinkRevision, waId, row.ModeRevision),
                WhatsAppAssistantActionKind.AskButtons => WhatsAppOutgoingMessage.CreateAssistantQuickReplies(row.SubjectUserId, contact.Id, row.RequestId,
                    step.Text!, [.. step.Buttons!.Select((title, button) => new WhatsAppOutgoingQuickReply(
                        WhatsAppAssistantCaseButtonCodec.Encode(row.RequestId, button), title, 0))],
                    requestedAtUtc, contact.LinkRevision, waId, row.ModeRevision),
                _ => null,
            };
            if (outgoing is not null) conversations.Add(outgoing);
            else if (step.Record.Kind == WhatsAppAssistantActionKind.Handoff)
                outgoing = await handoffs.HandOffAsync(contact, row.SubjectUserId, row.RequestId, step.Record.HandoffReasonCode ?? "case",
                    step.Record.HandoffReason, step.Text, ct);
            // After the checks above these cannot fail; if one did, it is a bug and the whole unit of work rolls back.
            if (outgoing is null)
            {
                // «No responder», or a handoff whose message could not go out: the chat shows it as a line, from the reference.
                if (!row.RecordOutcome(record) || !row.FinishWithoutReply(false, nowUtc))
                    throw new InvalidOperationException("A checked reply could not record its outcome.");
                continue;
            }

            outgoing.ExplainAssistantReply(record);
            if (!row.Ready(step.ProposalId, null, nowUtc) || !row.Accept(outgoing.Id, Fingerprint(row.RequestId, step.ProposalId), null, nowUtc))
                throw new InvalidOperationException("A checked reply could not be queued.");
        }

        return Result.Success();
    }

    /// <summary>
    /// A tap on a button of a case's question, inside the caller's unit of work and under its lock of the contact. A fixed text or a
    /// handoff is answered here with a reference of its own, and the tap is marked in the same commit. A button that sends a form
    /// returns false: the tap stays a message (its title) that the assistant interprets with the case in its read.
    /// </summary>
    public async Task<bool> ApplyTapAsync(WhatsAppContact contact, WhatsAppAssistantChoiceRow tap, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(tap);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var message = await messages.GetInboundAsync(tap.WaMessageId, ct);
        if (message?.Id != tap.Id || message.ContactId != contact.Id) return false;
        var original = WhatsAppAssistantCaseButtonCodec.TryParse(tap.ReplyId, out var requestId, out var index)
            ? await requests.GetByRequestAsync(requestId, ct) : null;
        var outgoing = original?.OutgoingId is { } outgoingId ? await conversations.GetOutgoingAsync(outgoingId, ct) : null;
        var sent = outgoing?.QuickReplies.SingleOrDefault(reply => reply.Id == tap.ReplyId);
        var record = WhatsAppAssistantReplyRecordCodec.Parse(outgoing?.AssistantReplyJson);
        var agent = await agents.FindMainAsync(ct);
        // A tap that is not on a question of the agent, or that comes when the conversation is not the assistant's, is only marked.
        if (original is not { Kind: WhatsAppAssistantRequestKind.Automatic, ActorUserId: { } actor, ContextJson: { } contextJson, ProposalId: { } proposalId }
            || original.ContactId != contact.Id || outgoing?.Origin != WhatsAppMessageOrigin.Assistant || sent is null
            || record is not { Kind: WhatsAppAssistantActionKind.AskButtons, CaseId: { } caseId } || agent is null
            || contact.Mode != WhatsAppConversationMode.Assistant || contact.UserId != original.SubjectUserId || contact.LinkRevision != original.LinkRevision
            || contact.WaId is not { } waId || await requests.GetBySourceAsync(contact.Id, tap.Id, WhatsAppAssistantRequestKind.Automatic, ct) is not null)
        {
            message.MarkAssistantProcessed(nowUtc);
            return true;
        }

        var culture = WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(original)).Culture;
        var button = agent.Cases.SingleOrDefault(item => item.Id == caseId && item.IsActive)?.Action.Buttons?.ElementAtOrDefault(index);
        // The case changed after the question went out: what the button does now is not what the person saw, so a person answers.
        var current = button is not null && button.Title.For(culture, agent.OriginalCulture) == sent.Title ? button : null;
        if (current?.Destination == WhatsAppAssistantButtonDestination.SendForm) return false;
        message.MarkAssistantProcessed(nowUtc);
        var text = current?.Destination == WhatsAppAssistantButtonDestination.ExactText ? current.Text?.For(culture, agent.OriginalCulture) : null;
        string? code = text is null ? "button" : null;
        if (text is not null)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(original)).TimeZoneId);
            var dayStartUtc = WhatsAppBusinessDay.StartUtc(WhatsAppBusinessDay.LocalDay(clock.GetUtcNow(), zone), zone);
            if (await history.CountRepliesAsync(contact.Id, dayStartUtc, ct) + 1 > agent.Limits.DailyReplyLimit) (text, code) = (null, "dailyReplyLimit");
        }

        var row = WhatsAppAssistantRequest.Create(Guid.CreateVersion7(), contact.Id, tap.Id, actor, original.SubjectUserId, WhatsAppAssistantRequestKind.Automatic,
            contact.ModeRevision, contact.LinkRevision, contextJson, nowUtc, nowUtc.AddDays(1));
        requests.Add(row);
        var why = WhatsAppAssistantReplyRecordCodec.Serialize(new(text is null ? WhatsAppAssistantActionKind.Handoff : WhatsAppAssistantActionKind.ExactText,
            WhatsAppAssistantCaseSource.Own, caseId, record.CaseTitle, "", [], 1, 1, code, code == "button" ? sent.Title : null, sent.Title));
        var reply = text is not null
            ? WhatsAppOutgoingMessage.CreateAssistantText(row.SubjectUserId, contact.Id, row.RequestId, text, nowUtc, contact.LinkRevision, waId, row.ModeRevision)
            : await handoffs.HandOffAsync(contact, row.SubjectUserId, row.RequestId, code!, code == "button" ? sent.Title : null,
                agent.Profile.HandoffMessage.For(culture, agent.OriginalCulture), ct);
        if (reply is null)
        {
            if (!row.RecordOutcome(why) || !row.FinishWithoutReply(false, nowUtc)) throw new InvalidOperationException("A tap could not record its outcome.");
            return true;
        }

        if (text is not null) conversations.Add(reply);
        reply.ExplainAssistantReply(why);
        if (!row.Ready(proposalId, null, nowUtc) || !row.Accept(reply.Id, Fingerprint(row.RequestId, proposalId), null, nowUtc))
            throw new InvalidOperationException("A tap could not be answered.");
        return true;
    }

    private static WhatsAppAssistantReplyStep? FromReply(Guid proposalId, WhatsAppAssistantReplyPayload reply, WhatsAppAssistantAgentRow agent, string culture)
    {
        if (Case(reply.CaseSource, reply.CaseId, agent) is not { } found) return null;
        var (source, caseId, title, action) = found;
        var handoffMessage = agent.Profile.HandoffMessage.For(culture, agent.OriginalCulture);
        // A button without a title in this language takes the original one; the domain keeps the original required.
        var titles = (action.Buttons ?? []).Select(button => button.Title.For(culture, agent.OriginalCulture)).OfType<string>()
            .Where(text => !string.IsNullOrWhiteSpace(text)).ToArray();
        WhatsAppAssistantReplyRecord Record(WhatsAppAssistantActionKind kind, string? code = null, string? handoffReason = null) =>
            new(kind, source, caseId, title, reply.Reason, [], 1, 1, code, handoffReason);
        // What goes out is the business's text as it is saved now, never the text the model saw.
        return reply.Kind switch
        {
            "exactText" when action.Kind == WhatsAppAssistantActionKind.ExactText && action.Text?.For(culture, agent.OriginalCulture) is { } text =>
                new(proposalId, Record(WhatsAppAssistantActionKind.ExactText), text, null),
            "askButtons" when action.Kind == WhatsAppAssistantActionKind.AskButtons && action.Question?.For(culture, agent.OriginalCulture) is { } question
                && titles.Length is >= 1 and <= 3 && titles.Length == action.Buttons!.Count =>
                new(proposalId, Record(WhatsAppAssistantActionKind.AskButtons), question, titles),
            "handoff" when reply.HandoffReasonCode == "case" && action.Kind == WhatsAppAssistantActionKind.Handoff =>
                new(proposalId, Record(WhatsAppAssistantActionKind.Handoff, "case", action.HandoffReason), handoffMessage, null),
            "handoff" when reply.HandoffReasonCode is "blockedTopic" or "unknown" =>
                new(proposalId, Record(WhatsAppAssistantActionKind.Handoff, reply.HandoffReasonCode), handoffMessage, null),
            "noReply" when action.Kind == WhatsAppAssistantActionKind.NoReply => new(proposalId, Record(WhatsAppAssistantActionKind.NoReply), null, null),
            _ => null,
        };
    }

    private static WhatsAppAssistantReplyStep? FromDraft(AssistantObservedProposal proposal, WhatsAppAssistantDraftPayload draft, WhatsAppAssistantAgentRow agent,
        string culture, out string? failure)
    {
        failure = null;
        if (draft.Problems.Count > 0 || Case(draft.CaseSource, draft.CaseId, agent) is not { } found
            || found.Action.Kind != WhatsAppAssistantActionKind.DraftedReply) return null;
        var (source, caseId, title, action) = found;
        // The facts the model read in its turn, plus what the business wrote: what the text may name.
        var facts = proposal.ObservedReads.Where(read => read.TurnId == proposal.TurnId && read.Capability == Knowledge)
            .Select(read => read.Payload).OfType<WhatsAppAssistantKnowledgePayload>().SelectMany(read => read.Facts).Select(fact => fact.Text);
        var problems = WhatsAppDraftedReplyPolicy.Problems(draft.Text, culture, agent.Limits.MaxReplyLength,
            facts.Concat([agent.Profile.Name, agent.Profile.Introduction, agent.Profile.Does, action.Instructions ?? ""]));
        if (problems.Count > 0)
        {
            failure = problems.Contains("tooLong") ? "replyTooLong" : "replyNotAllowed";
            return null;
        }

        return new(proposal.Id, new(WhatsAppAssistantActionKind.DraftedReply, source, caseId, title, draft.Reason, draft.Used, 1, 1), draft.Text.Trim(), null);
    }

    private static (WhatsAppAssistantCaseSource Source, Guid? CaseId, string? Title, WhatsAppAssistantAction Action)? Case(string source, Guid? caseId,
        WhatsAppAssistantAgentRow agent)
    {
        if (source == "fallback" && caseId is null)
            return agent.Limits.AllowedActions.Contains(agent.Fallback.Kind) ? (WhatsAppAssistantCaseSource.Fallback, null, null, agent.Fallback) : null;
        var found = source == "own" ? agent.Cases.SingleOrDefault(item => item.Id == caseId && item.IsActive) : null;
        return found is null || !agent.Limits.AllowedActions.Contains(found.Action.Kind) ? null
            : (WhatsAppAssistantCaseSource.Own, found.Id, found.When, found.Action);
    }

    private static (WhatsAppAssistantCaseSource Source, Guid? CaseId, string? Title) First(AssistantCapabilityPayload payload) => payload switch
    {
        WhatsAppAssistantReplyPayload { CaseSource: "own" } reply => (WhatsAppAssistantCaseSource.Own, reply.CaseId, reply.CaseTitle),
        WhatsAppAssistantDraftPayload { CaseSource: "own" } draft => (WhatsAppAssistantCaseSource.Own, draft.CaseId, draft.CaseTitle),
        _ => (WhatsAppAssistantCaseSource.Fallback, null, null),
    };

    private static string Fingerprint(Guid requestId, Guid proposalId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestId.ToString("N") + proposalId.ToString("N"))));
}
