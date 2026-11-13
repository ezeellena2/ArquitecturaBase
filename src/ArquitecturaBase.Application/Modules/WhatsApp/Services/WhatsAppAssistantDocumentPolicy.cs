using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// Task 9 of the documents (decision 11): what is said about a read document, outside any channel transaction. Formularios registers it in
/// the inbox and decides what to answer; this turns that into fixed texts in the person's language: sure, the prefilled form with the
/// document attached by the server; doubt, «¿Qué es este documento?» with its candidates (buttons, or a list when a name does not fit a
/// button); not recognized, the list of what the person can complete and «Otro»; not legible, which datum to send again; repeated, when
/// and where it was loaded; not sendable, its text and a handoff; the day's quota, its text. A choice sends the chosen candidate's form
/// without another interpretation; «Otro» leaves it unclassified; a form chosen from the list continues the operation with that form fixed.
/// </summary>
internal sealed class WhatsAppAssistantDocumentPolicy(IStructuredDocumentChannel documents, WhatsAppFormSendPolicy sending,
    IWhatsAppFlowEnvironment environment, IAssistantOperations operations, IWhatsAppAssistantReader reader, TimeProvider clock,
    WhatsAppAssistantSpreadsheetPolicy spreadsheets)
{
    /// <summary>The handoff's stable reason code when the form cannot be sent to the person (CE4-A).</summary>
    public const string NotSendableCode = "documentNotSendable";
    public static readonly AssistantCapabilityReference ProposeEntry = new("forms.documents.propose-entry", 1);
    private const int MaximumButtonTitle = 20;

    public static bool Handles(AssistantCapabilityReference capability) => capability == ProposeEntry;

    /// <summary>
    /// The interpretation of a read document closed: its proposal (the documents' one, or none when a document alone closed without one)
    /// goes to the inbox with the copy and the reading, and the inbox says what to tell the person.
    /// </summary>
    public async Task<Result<WhatsAppAssistantTerminalResponse?>> RegisterAsync(WhatsAppAssistantReferenceRow reference, AssistantOperationResponse operation,
        AssistantChannelContext context, CancellationToken ct)
    {
        if (reference.AttachmentMessageId is not { } messageId || await reader.FindDocumentAsync(reference.ContactId, messageId, ct) is not { } document)
            return WhatsAppAssistantErrors.InvalidProposal;
        var proposal = operation.Proposal is { } observed && Handles(observed.Capability) ? observed : null;
        var expiresAtUtc = ReplyExpiresAtUtc(reference);
        var registration = await documents.RegisterAsync(new StructuredDocumentRegistrationRequest(WhatsAppReceivedDocumentPreparationPolicy.ChannelId,
            SourceId(document), reference.SubjectUserId, context.Culture, document.ReceivedAtUtc, Copy(document))
        {
            // A form chosen for a document that was not recognized: its proposal recognizes it only for that form.
            Reading = Reading(document), Proposal = proposal, ReplyExpiresAtUtc = expiresAtUtc, FocusDefinitionId = context.FocusDefinitionId,
        }, ct);
        var proposalId = proposal?.Id ?? reference.RequestId;
        StructuredRecordAssistantProposal? recognized = proposal is not null && operation.Provenance is { } provenance
            ? new(proposal.Id, proposal.Capability.Id, proposal.Capability.Version, provenance) : null;
        return await ReplyAsync(reference, document, registration, proposalId, recognized, context, Deadline(registration, expiresAtUtc), ct);
    }

    /// <summary>Review of task 9: what a tap on a question that changed, or that another tap already answered, is told.</summary>
    public static WhatsAppAssistantTerminalResponse Replaced(WhatsAppAssistantReferenceRow question, Guid proposalId)
    {
        var culture = WhatsAppAssistantSuggestionPolicy.Context(question).Culture;
        return new(proposalId, Text("DocumentReplaced", culture), culture);
    }

    /// <summary>
    /// A tap on an option of the latest document question: a candidate goes as the sure one, without another interpretation; «Otro» leaves
    /// it unclassified; a form chosen when it was not recognized continues the operation with that form fixed. A question is answered once,
    /// like a clarification: another tap on it, or one on a question that is not the latest, is told it changed. Null, without asking the
    /// inbox anything, when an operator took over, the number was unlinked or the question expired, or it no longer exists. Closing task 9:
    /// what the inbox or the engine cannot do now is said («No pude preparar el formulario»), and only an applied choice closes the question.
    /// </summary>
    public async Task<WhatsAppAssistantDocumentChoice?> ChooseAsync(Guid contactId, WhatsAppAssistantChoiceRow tap, CancellationToken ct)
    {
        if (!WhatsAppAssistantDocumentReplyCodec.TryParse(tap.ReplyId, out var requestId, out var proposalId, out var definitionId)) return null;
        var reference = await reader.FindAsync(contactId, requestId, ct);
        if (reference is not { Kind: WhatsAppAssistantRequestKind.Automatic, State: WhatsAppAssistantRequestState.Sent } || reference.ProposalId != proposalId
            || reference.AttachmentMessageId is not { } messageId)
            return null;
        var state = await reader.FindDocumentTapStateAsync(reference, tap.Id, clock.GetUtcNow().UtcDateTime, ct);
        if (state == WhatsAppAssistantDocumentTapState.Closed) return null;
        if (state == WhatsAppAssistantDocumentTapState.Answered || await reader.FindLatestDocumentRequestAsync(contactId, ct) != requestId)
            return new(reference, Replaced(reference, proposalId), null);
        var context = WhatsAppAssistantSuggestionPolicy.Context(reference);
        var culture = context.Culture;
        // VC1-A (task 29): the question of a spreadsheet none of whose rows fit.
        if (await reader.FindDocumentAsync(contactId, messageId, ct) is not { } document)
            return await spreadsheets.ChooseAsync(reference, proposalId, definitionId, context, ct) ?? new(reference, NotPrepared(proposalId, culture), null);
        var registration = await documents.RegisterAsync(new StructuredDocumentRegistrationRequest(WhatsAppReceivedDocumentPreparationPolicy.ChannelId,
            SourceId(document), reference.SubjectUserId, culture, document.ReceivedAtUtc, Copy(document))
        {
            Reading = Reading(document), ReplyExpiresAtUtc = ReplyExpiresAtUtc(reference),
        }, ct);
        if (registration.DocumentId is not { } documentId) return new(reference, NotPrepared(proposalId, culture), null);
        // The form goes only while the inbox waits for its person, and never outlives that deadline as the inbox kept it.
        var expiresAtUtc = Deadline(registration, ReplyExpiresAtUtc(reference));
        if (definitionId is null)
        {
            var chosen = await documents.ChooseOtherAsync(new(documentId, WhatsAppReceivedDocumentPreparationPolicy.ChannelId, SourceId(document),
                reference.SubjectUserId, culture, clock.GetUtcNow().UtcDateTime), ct);
            return chosen == StructuredDocumentChoiceState.Unavailable ? new(reference, NotPrepared(proposalId, culture), null)
                : new(reference, new(proposalId, Text("DocumentOtherChosen", culture), culture), null) { Applies = true };
        }
        // Doubt: the candidate goes as the sure one with the proposal that recognized it, which the operation keeps.
        if (registration.Kind == StructuredDocumentReplyKind.Doubt)
        {
            if (registration.Forms.FirstOrDefault(form => form.DefinitionId == definitionId) is not { } form)
                return new(reference, NotPrepared(proposalId, culture), null);
            if (expiresAtUtc <= clock.GetUtcNow().UtcDateTime) return new(reference, Expired(proposalId, culture), null);
            var operation = await operations.GetAsync(context, requestId, ct);
            if (operation.IsFailure || operation.Value is not { Proposal: { } proposal, Provenance: { } provenance } || proposal.Id != proposalId
                || !Handles(proposal.Capability)) return new(reference, NotPrepared(proposalId, culture), null);
            var send = await SendAsync(reference, document, documentId, form, new(proposal.Id, proposal.Capability.Id, proposal.Capability.Version, provenance),
                proposalId, Text("DocumentChosen", culture), culture, expiresAtUtc, ct);
            // Applied when its form goes, or when it cannot be sent to the person and goes to the team (a handoff).
            return new(reference, send, null) { Applies = send.Send is not null || send.HandOff is not null };
        }
        // Not recognized: the next interpretation continues this operation with the chosen form fixed.
        if (registration.Kind == StructuredDocumentReplyKind.Unknown && registration.Forms.Any(form => form.DefinitionId == definitionId))
            return new(reference, null, definitionId) { Applies = true };
        return new(reference, NotPrepared(proposalId, culture), null);
    }

    // What to say for what the inbox decided.
    private async Task<Result<WhatsAppAssistantTerminalResponse?>> ReplyAsync(WhatsAppAssistantReferenceRow reference, WhatsAppAssistantDocumentRow document,
        StructuredDocumentRegistration registration, Guid proposalId, StructuredRecordAssistantProposal? recognized, AssistantChannelContext context,
        DateTime expiresAtUtc, CancellationToken ct)
    {
        var culture = context.Culture;
        switch (registration.Kind)
        {
            case StructuredDocumentReplyKind.Sure when registration.DocumentId is { } documentId && registration.Forms is [var form] && recognized is not null:
                if (expiresAtUtc <= clock.GetUtcNow().UtcDateTime) return Expired(proposalId, culture);
                var body = registration.Summary is { Length: > 0 } summary ? summary : Text("DocumentSure", culture, form.Name);
                return await SendAsync(reference, document, documentId, form, recognized, proposalId, body, culture, expiresAtUtc, ct);
            case StructuredDocumentReplyKind.Doubt when registration.Forms.Count is 2 or 3:
                return Question(reference.RequestId, proposalId, Text("DocumentDoubt", culture), registration.Forms, culture);
            case StructuredDocumentReplyKind.Unknown when registration.Forms.Count > 0:
                return Question(reference.RequestId, proposalId, Text("DocumentUnknown", culture), registration.Forms, culture, list: true);
            case StructuredDocumentReplyKind.Unknown when registration.DocumentId is { } documentId:
                // Nothing the person can complete: «Otro» at once.
                var chosen = await documents.ChooseOtherAsync(new(documentId, WhatsAppReceivedDocumentPreparationPolicy.ChannelId, SourceId(document),
                    reference.SubjectUserId, culture, clock.GetUtcNow().UtcDateTime), ct);
                return chosen == StructuredDocumentChoiceState.Unavailable ? Result.Success<WhatsAppAssistantTerminalResponse?>(null)
                    : new WhatsAppAssistantTerminalResponse(proposalId, Text("DocumentOtherChosen", culture), culture);
            case StructuredDocumentReplyKind.Unreadable:
                return new WhatsAppAssistantTerminalResponse(proposalId, registration.MissingDatum is { Length: > 0 } datum
                    ? Text("DocumentUnreadableDatum", culture, datum) : Text("DocumentNotLegible", culture), culture);
            case StructuredDocumentReplyKind.Duplicate when registration.Duplicate is { } duplicate:
                var date = Date(duplicate.AtUtc, context.TimeZoneId, culture);
                return new WhatsAppAssistantTerminalResponse(proposalId, duplicate.RecordNumber is { Length: > 0 } number
                    ? Text("DocumentDuplicate", culture, date, number) : Text("DocumentDuplicateDate", culture, date), culture);
            case StructuredDocumentReplyKind.NotSendable when registration.Forms is [var form]:
                return NotSendable(proposalId, form, culture);
            case StructuredDocumentReplyKind.Quota:
                return new WhatsAppAssistantTerminalResponse(proposalId, Text("DocumentQuota", culture), culture);
            default:
                // Rejected was answered when the file was received, and what the inbox could not register is not said.
                return Result.Success<WhatsAppAssistantTerminalResponse?>(null);
        }
    }

    // The chosen form prepared by formularios with what was read, its marks and the question the document goes to, resolved for the send.
    private async Task<WhatsAppAssistantTerminalResponse> SendAsync(WhatsAppAssistantReferenceRow reference, WhatsAppAssistantDocumentRow document, Guid documentId,
        StructuredDocumentFormChoice form, StructuredRecordAssistantProposal recognized, Guid proposalId, string body, string culture, DateTime expiresAtUtc,
        CancellationToken ct)
    {
        var prepared = await documents.PrepareSendAsync(new(documentId, WhatsAppReceivedDocumentPreparationPolicy.ChannelId, SourceId(document),
            reference.SubjectUserId, culture, form.DefinitionId), ct);
        if (prepared.State == StructuredDocumentSendState.NotSendable) return NotSendable(proposalId, form, culture);
        if (prepared.State == StructuredDocumentSendState.Expired) return Expired(proposalId, culture);
        if (prepared is not { State: StructuredDocumentSendState.Ready, Prefill: { } prefill })
            return new(proposalId, Text("DocumentNotPrepared", culture), culture);
        var context = await sending.ResolveProposedAsync(reference.ContactId, prefill, null, ct);
        if (context.IsFailure) return new(proposalId, Text("DocumentNotPrepared", culture), culture);
        var file = prepared.DocumentFieldId is { } fieldId && WhatsAppReceivedDocumentPreparationPolicy.TryParseKey(document.StorageKey, out _, out var kind)
            ? new WhatsAppFlowServerFile(fieldId, document.StorageKey, document.FileName, WhatsAppReceivedDocumentPreparationPolicy.MediaType(kind), document.Size, document.Sha256)
            : null;
        return new(proposalId, body, culture)
        {
            Send = new(documentId, context.Value, prefill, prepared.Marks, file, recognized, Text("DocumentReason", culture, document.FileName), expiresAtUtc),
        };
    }

    // Closing task 9: what the inbox or the engine could not do now (a temporary failure, or an answer that changed under the question).
    private static WhatsAppAssistantTerminalResponse NotPrepared(Guid proposalId, string culture) => new(proposalId, Text("DocumentNotPrepared", culture), culture);

    // The inbox no longer waits for its person (its deadline passed): the document is for the team, and no form goes.
    private static WhatsAppAssistantTerminalResponse Expired(Guid proposalId, string culture) => new(proposalId, Text("DocumentExpired", culture), culture);

    private static WhatsAppAssistantTerminalResponse NotSendable(Guid proposalId, StructuredDocumentFormChoice form, string culture) =>
        new(proposalId, Text("DocumentNotSendable", culture, form.Name), culture)
        {
            HandOff = new(NotSendableCode, Fit(Text("DocumentNotSendableReason", culture, form.Name), 200)),
        };

    // Two or three candidates with short names go as buttons (with two, the third is «Otro»); a longer name, or the forms of a document
    // that was not recognized (up to nine and «Otro»), as a list.
    internal static WhatsAppAssistantTerminalResponse Question(Guid requestId, Guid proposalId, string body, IReadOnlyList<StructuredDocumentFormChoice> forms,
        string culture, bool list = false)
    {
        var options = forms.Take(9).Select(form => new WhatsAppOutgoingQuickReply(WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposalId, form.DefinitionId),
            form.Name, 0)).ToList();
        if (options.Count < 3 || list) options.Add(new(WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposalId, null), Text("DocumentOther", culture), 0));
        if (!list && options.Count <= 3 && options.All(option => option.Title.Length <= MaximumButtonTitle)
            && options.Select(option => option.Title).Distinct(StringComparer.Ordinal).Count() == options.Count)
            return new(proposalId, body, culture) { Buttons = options };
        return new(proposalId, body, culture) { List = Rows(options), ListButton = Text("DocumentChooseForm", culture) };
    }

    // The rows of a list stay apart: a title fits WhatsApp's 24 characters, a cut one keeps the full name under it (up to 72), and titles
    // that still repeat (the same name, or the same start) are numbered in order.
    private static WhatsAppOutgoingQuickReply[] Rows(List<WhatsAppOutgoingQuickReply> options)
    {
        var rows = options.Select(option => option with
        {
            Title = Fit(option.Title, WhatsAppOutgoingMessage.MaxListRowTitleLength),
            Description = option.Title.Length > WhatsAppOutgoingMessage.MaxListRowTitleLength ? Fit(option.Title, WhatsAppOutgoingMessage.MaxListRowDescriptionLength) : null,
        }).ToArray();
        var repeated = rows.GroupBy(row => row.Title, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < rows.Length; index++)
        {
            if (!repeated.Contains(rows[index].Title)) continue;
            var number = numbers[rows[index].Title] = numbers.GetValueOrDefault(rows[index].Title) + 1;
            var suffix = string.Create(CultureInfo.InvariantCulture, $" ({number})");
            rows[index] = rows[index] with { Title = Fit(options[index].Title, WhatsAppOutgoingMessage.MaxListRowTitleLength - suffix.Length) + suffix };
        }
        return rows;
    }

    // The reply deadline is the expiry of the send that may follow: the session lifetime, bounded by the reference's content.
    private DateTime ReplyExpiresAtUtc(WhatsAppAssistantReferenceRow reference)
    {
        var expiresAtUtc = clock.GetUtcNow().UtcDateTime.Add(environment.SessionLifetime);
        return reference.ContentExpiresAtUtc < expiresAtUtc ? reference.ContentExpiresAtUtc : expiresAtUtc;
    }

    // Review of task 9: the deadline the inbox kept for its person (its first registration's) bounds any form sent later; without one, the
    // one asked for now.
    private static DateTime Deadline(StructuredDocumentRegistration registration, DateTime requested) =>
        registration.ReplyExpiresAtUtc is { } kept && kept < requested ? kept : requested;

    private static string SourceId(WhatsAppAssistantDocumentRow document) => document.MessageId.ToString("N");

    private static StructuredDocumentCopy Copy(WhatsAppAssistantDocumentRow document)
    {
        var kind = WhatsAppReceivedDocumentPreparationPolicy.TryParseKey(document.StorageKey, out _, out var parsed) ? parsed
            : throw new InvalidOperationException("The stored document copy has an unknown key.");
        return new(document.StorageKey, document.FileName, WhatsAppReceivedDocumentPreparationPolicy.MediaType(kind), document.Size, document.Sha256,
            kind == StructuredDocumentKind.Pdf ? document.Pages : null);
    }

    private static StructuredDocumentReading Reading(WhatsAppAssistantDocumentRow document)
    {
        try
        {
            return JsonSerializer.Deserialize<StructuredDocumentReading>(document.ReadingJson, WhatsAppReceivedDocumentReadingPolicy.ReadingJson)
                ?? throw new InvalidOperationException("The stored document reading is missing.");
        }
        catch (JsonException exception) { throw new InvalidOperationException("The stored document reading is invalid.", exception); }
    }

    // Dates only with numbers, in the conversation's zone: dd/MM/yyyy in Spanish and MM/dd/yyyy in English.
    internal static string Date(DateTime atUtc, string timeZoneId, string culture)
    {
        var local = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone) ? TimeZoneInfo.ConvertTimeFromUtc(atUtc, zone) : atUtc;
        return local.ToString(culture == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    internal static string Text(string key, string culture, params object[] values) => values.Length == 0 ? AssistantTerminalTexts.Get(key, culture)
        : DisplayNumbers.Format(culture, AssistantTerminalTexts.Get(key, culture), values);

    private static string Fit(string text, int maximum)
    {
        if (text.Length <= maximum) return text;
        var cut = maximum - 1;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut].TrimEnd() + "…";
    }
}

/// <summary>
/// What a tap on a document question decided (task 9): the reference it answers and either what to say or send now, or the form the next
/// interpretation fixes (a form chosen when the document was not recognized). <see cref="Applies"/> when the choice changed the document
/// (its form goes or goes to the team, «Otro» was recorded, or the form is fixed): only then does it close the question.
/// </summary>
internal sealed record WhatsAppAssistantDocumentChoice(WhatsAppAssistantReferenceRow Reference, WhatsAppAssistantTerminalResponse? Reply, Guid? FocusDefinitionId)
{
    public bool Applies { get; init; }

    public override string ToString() => "WhatsAppAssistantDocumentChoice { Content = [REDACTED] }";
}
