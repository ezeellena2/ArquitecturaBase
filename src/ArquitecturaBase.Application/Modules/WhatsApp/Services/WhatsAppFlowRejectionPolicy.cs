using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// Task 9c of the documents (point 6): what a capture through WhatsApp rejected answers, in the unit of work that records the rejection
/// (<see cref="WhatsAppFlowReplyStateService"/>). A fixed validation (<see cref="StructuredRecordCaptureResponse.FixedValidation"/>), whether
/// the send came from a document or not, sends the form again in a new send: its message says the reason and it opens with what the person
/// sent and the mark of the question. A repeated capture only gets its notice; any other rejection, nothing. The new send continues the
/// rejected one with a token of its own (<see cref="WhatsAppFlowSessionIssuer.Again"/>) and goes out from its sender: an operator's as any
/// message of that operator; the assistant's with a reference of its own on the reply, which carries the one that sent the form, so the
/// reservation checks it as that send and it is no offer. Only the reply that rejected its send, once, while the send lives. Like W3
/// (<see cref="WhatsAppSharedContentGuard"/>), nothing is queued that the reservation (<see cref="WhatsAppOutgoingSendGuard"/>) would refuse
/// for its sender: the operator's new message itself, or the first message of the assistant's send. It decides and changes the entities;
/// the caller saves, and nothing here calls Meta.
/// </summary>
internal sealed class WhatsAppFlowRejectionPolicy(IWhatsAppAssistantRepository requests, IWhatsAppConversationRepository conversations,
    IWhatsAppFlowSessionRepository sessions, WhatsAppFlowSessionIssuer issuer, WhatsAppOutgoingSendGuard reservation,
    WhatsAppContentRetention retention, TimeProvider clock)
{
    public static WhatsAppFlowRejectionAnswer Decide(StructuredRecordCaptureResponse capture) =>
        capture.State != "Rejected" ? WhatsAppFlowRejectionAnswer.None
        : capture.FixedValidation == true ? WhatsAppFlowRejectionAnswer.Resend
        : capture.Repetition is not null ? WhatsAppFlowRejectionAnswer.Notice
        : WhatsAppFlowRejectionAnswer.None;

    public async Task QueueAsync(WhatsAppMessage reply, WhatsAppFlowSession rejected, WhatsAppFlowRejection rejection, CancellationToken ct)
    {
        var answer = Decide(rejection.Capture);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        if (answer == WhatsAppFlowRejectionAnswer.None || reply.ProcessedAtUtc.HasValue || reply.ContactId != rejected.ContactId
            || rejected.CaptureState != WhatsAppFlowCaptureState.Rejected || rejected.ResponseWaMessageId != reply.WaMessageId
            || rejected.ExpiresAtUtc <= nowUtc) return;
        if (rejected.Origin != WhatsAppMessageOrigin.Assistant)
        {
            // A repeated capture comes only from a document or a row, which the assistant sends: an operator's send answers a fixed validation.
            // Its request is the reply it answers, so the same reply never queues it twice.
            var sender = rejected.OperatorUserId!.Value;
            if (answer != WhatsAppFlowRejectionAnswer.Resend || await conversations.GetByRequestAsync(rejected.ContactId, sender, reply.Id, ct) is not null
                || await conversations.GetForUpdateAsync(rejected.ContactId, ct) is not { } chat) return;
            var again = Again(reply, rejected, rejection);
            var flow = Attached(WhatsAppOutgoingMessage.CreateFlow(sender, rejected.ContactId, reply.Id, Body(rejected, rejection.Capture), again.Id, nowUtc), again);
            // As any message of the operator: in Atención manual, with the window open and the operator still allowed to send.
            if ((await reservation.ValidateAsync(chat, flow, ct)).IsFailure) return;
            sessions.Add(again);
            conversations.Add(flow);
            return;
        }
        if (await requests.GetBySourceAsync(rejected.ContactId, reply.Id, WhatsAppAssistantRequestKind.Automatic, ct) is not null
            || await requests.GetBySessionAsync(rejected.ContactId, rejected.Id, ct) is not { Kind: WhatsAppAssistantRequestKind.Automatic,
                ContextJson: { } contextJson, ActorUserId: not null, OutgoingId: { } firstId } sent
            || nowUtc >= sent.ContentExpiresAtUtc
            || await conversations.GetOutgoingAsync(firstId, ct) is not { Origin: WhatsAppMessageOrigin.Assistant, RecipientUserId: { } recipient,
                RecipientLinkRevision: { } linkRevision, RecipientWaId: { } waId, AssistantModeRevision: { } modeRevision } first
            // What answers continues that send from the same sender and revisions, so it goes out only while its first message still
            // passes the reservation: Asistente with the same mode revision (no operator took the chat), the link, its context and content.
            || await conversations.GetForUpdateAsync(rejected.ContactId, ct) is not { } contact
            || (await reservation.ValidateAsync(contact, first, ct)).IsFailure) return;
        var reference = WhatsAppAssistantRequest.CreateCaptureReply(sent, Guid.CreateVersion7(), reply.Id, nowUtc);
        WhatsAppOutgoingMessage outgoing;
        if (answer == WhatsAppFlowRejectionAnswer.Resend)
        {
            var again = Again(reply, rejected, rejection);
            outgoing = Attached(WhatsAppOutgoingMessage.CreateAssistantFlow(recipient, rejected.ContactId, reference.RequestId, Body(rejected, rejection.Capture),
                again.Id, nowUtc, linkRevision, waId, modeRevision), again);
        }
        else
        {
            if (Notice(rejected.Culture, contextJson, rejection.Capture.Repetition!) is not { } notice) return;
            outgoing = WhatsAppOutgoingMessage.CreateAssistantText(recipient, rejected.ContactId, reference.RequestId, notice, nowUtc, linkRevision, waId, modeRevision);
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference.RequestId.ToString("N") + reference.RequestId.ToString("N"))));
        if (!reference.Ready(reference.RequestId, answer == WhatsAppFlowRejectionAnswer.Resend ? rejected.DefinitionId : null, nowUtc)
            || !reference.Accept(outgoing.Id, fingerprint, null, nowUtc)) return;
        if (outgoing.FlowSession is { } opened) sessions.Add(opened);
        requests.Add(reference);
        conversations.Add(outgoing);
    }

    // The new send: the origin of the rejected one (an operator's without a proposal: the operator, with nothing proposed), the file the
    // server adds on capture, what the person sent and the mark of the question that did not pass, with the notice that asks to correct it.
    // Its content lives as long as the rejected one's; an operator's had none, so it lives as long as the reply that brought what the person
    // sent, as an assistant's lives as long as its source message: past the deadline, so a reply sent in time and processed late is read.
    private WhatsAppFlowSession Again(WhatsAppMessage reply, WhatsAppFlowSession rejected, WhatsAppFlowRejection rejection)
    {
        var capture = rejection.Capture;
        var content = rejection.Content;
        var server = new WhatsAppFlowServerData(content?.Server?.File,
            capture.RejectedFieldId is { } fieldId ? [new StructuredDocumentFieldMark(fieldId, StructuredDocumentMarkKind.Invalid, capture.ErrorCode)] : null)
        { Spreadsheet = content?.Server?.Spreadsheet, Correction = true };
        var origin = content?.Origin ?? new StructuredRecordCaptureOrigin(StructuredRecordSenderKind.Manual, rejected.OperatorUserId, null, null, [], []);
        var opened = new WhatsAppFlowAssistantContent(1, origin, content?.ProposedFields ?? []) { Server = server, Answers = Answers(rejection) };
        return issuer.Again(rejected, rejection.Payload, opened,
            rejected.AssistantContentExpiresAtUtc ?? reply.OccurredAtUtc.AddDays(retention.MessageRetentionDays));
    }

    // What the person sent, as the new send's initial values: each question the prefill takes, with a value it takes.
    private static IReadOnlyList<StructuredRecordProposedValue> Answers(WhatsAppFlowRejection rejection) =>
        [.. rejection.Projection.Mapping
            .Where(field => rejection.Answers.TryGetValue(field.Key, out var value) && value.Length > 0
                && WhatsAppFlowPrefillPolicy.Build(rejection.Projection, new Dictionary<string, string>(StringComparer.Ordinal) { [field.Key] = value }) is
                    { IsSuccess: true, Value.Count: > 0 })
            .Select(field => new StructuredRecordProposedValue(field.FieldId, field.Type, rejection.Answers[field.Key]))];

    // The reason, as the capture's error says it in the send's language, and the form to correct.
    private static string Body(WhatsAppFlowSession rejected, StructuredRecordCaptureResponse capture)
    {
        var culture = rejected.Culture;
        var names = JsonSerializer.Deserialize<Dictionary<string, string>>(rejected.NameJson, JsonSerializerOptions.Web);
        if (names is null || !names.TryGetValue(culture, out var formName)) throw new InvalidOperationException("The send keeps the name of its form in its language.");
        var reason = WhatsAppFlowMarks.Invalid(new(Guid.Empty, StructuredDocumentMarkKind.Invalid, capture.ErrorCode), culture);
        return reason + " " + DisplayNumbers.Format(culture, FlowTexts.Get("FixedValidationResend", culture), formName);
    }

    // The documents' notice of a repeated receipt, with the date only in numbers in the person's time zone.
    private static string? Notice(string culture, string contextJson, StructuredRecordRepetition repetition)
    {
        AssistantChannelContext? context;
        try { context = JsonSerializer.Deserialize<AssistantChannelContext>(contextJson, JsonSerializerOptions.Web); }
        catch (JsonException) { return null; }
        if (context is null) return null;
        var date = WhatsAppAssistantDocumentPolicy.Date(repetition.AtUtc, context.TimeZoneId, culture);
        return repetition.RecordNumber is { Length: > 0 } number
            ? WhatsAppAssistantDocumentPolicy.Text("DocumentDuplicate", culture, date, number)
            : WhatsAppAssistantDocumentPolicy.Text("DocumentDuplicateDate", culture, date);
    }

    private static WhatsAppOutgoingMessage Attached(WhatsAppOutgoingMessage outgoing, WhatsAppFlowSession session)
    {
        outgoing.AttachFlowSession(session);
        return outgoing;
    }
}
