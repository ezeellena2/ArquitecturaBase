using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// VC1-A (task 29): what is said about the rows of a spreadsheet, outside any channel transaction. Formularios registers its rows in the inbox
/// (<see cref="IStructuredDocumentRowsChannel"/>) and decides what became of each; this turns it into fixed texts in the person's language:
/// the notice (how many rows of which form, what goes, what was repeated and what went to the team), which the model does not write, and up to
/// three loads, each its form prepared like a document's («1 de 3 · …», with its form in the footer), in the order of the spreadsheet. None
/// fits: «No lo reconoce» with the list and «Otro»; the same spreadsheet again: when it arrived; the day's quota: its text. The loads answer
/// the file the person sent: they are no offers and spend none.
/// </summary>
internal sealed class WhatsAppAssistantSpreadsheetPolicy(IStructuredDocumentRowsChannel rows, IStructuredDocumentChannel documents,
    WhatsAppFormSendPolicy sending, IWhatsAppFlowEnvironment environment, IAssistantOperations operations, IWhatsAppAssistantReader reader, TimeProvider clock)
{
    public static readonly AssistantCapabilityReference ProposeRows = new("forms.documents.propose-rows", 1);
    private const int MaximumBody = 1024;
    private static readonly StructuredDocumentRowKind[] ForTeam =
        [StructuredDocumentRowKind.NotSendable, StructuredDocumentRowKind.OverLimit, StructuredDocumentRowKind.Unmatched, StructuredDocumentRowKind.Unseen];

    public static bool Handles(AssistantCapabilityReference capability) => capability == ProposeRows;

    /// <summary>Whether a stored spreadsheet was prepared to load its rows (decision 13: its person could load when it was downloaded).</summary>
    public static bool Loads(WhatsAppAssistantSpreadsheetRow? sheet) => sheet is not null && Prepared(sheet)?.DataRowCount is not null;

    /// <summary>
    /// The interpretation of a spreadsheet to load closed: its rows go to the inbox, which says what became of each. Without a proposal (a
    /// spreadsheet alone that closed without one) the inbox leaves it unclassified with the forms to choose. A spreadsheet the inbox registered
    /// before (an attempt whose transaction did not commit, another host, or the newer message that replaced its reference) is answered from what
    /// the inbox kept, unless a reference that carries it was already answered.
    /// </summary>
    public async Task<Result<WhatsAppAssistantTerminalResponse?>> RegisterAsync(WhatsAppAssistantReferenceRow reference, WhatsAppAssistantSourceRow source,
        AssistantOperationResponse operation, AssistantChannelContext context, CancellationToken ct)
    {
        var proposal = operation.Proposal;
        if (reference.AttachmentMessageId is not { } messageId || source.Spreadsheet is not { } sheet || sheet.MessageId != messageId
            || proposal is not null && (!Handles(proposal.Capability) || operation.Provenance is null)
            || Prepared(sheet) is not { DataRowCount: not null } prepared) return WhatsAppAssistantErrors.InvalidProposal;
        var culture = context.Culture;
        var proposalId = proposal?.Id ?? reference.RequestId;
        var expiresAtUtc = ReplyExpiresAtUtc(reference);
        var registration = await rows.RegisterRowsAsync(Request(reference, source, sheet, prepared, culture, proposal, expiresAtUtc), ct);
        if (await AnsweredAsync(reference, messageId, registration, ct)) return Result.Success<WhatsAppAssistantTerminalResponse?>(null);
        switch (registration.Kind)
        {
            case StructuredDocumentReplyKind.Sure when registration.Rows.Count > 0 && proposal is not null:
                var deadline = registration.ReplyExpiresAtUtc is { } kept && kept < expiresAtUtc ? kept : expiresAtUtc;
                var recognized = new StructuredRecordAssistantProposal(proposal.Id, proposal.Capability.Id, proposal.Capability.Version, operation.Provenance!);
                var (loads, said) = deadline > clock.GetUtcNow().UtcDateTime
                    ? await LoadsAsync(reference, sheet, prepared, registration, recognized, culture, deadline, ct) : ([], Said(registration, []));
                return new WhatsAppAssistantTerminalResponse(proposal.Id, Notice(said, context.TimeZoneId, culture), culture)
                {
                    Loads = loads, SpreadsheetRowsJson = WhatsAppSpreadsheetRowsCodec.Serialize(said),
                };
            case StructuredDocumentReplyKind.Unknown when registration.Forms.Count > 0:
                return WhatsAppAssistantDocumentPolicy.Question(reference.RequestId, proposalId, Text("SpreadsheetUnknown", culture), registration.Forms, culture, list: true);
            case StructuredDocumentReplyKind.Unknown when registration.DocumentId is { } documentId:
                // Nothing the person can complete: «Otro» at once.
                var other = await documents.ChooseOtherAsync(new(documentId, WhatsAppReceivedDocumentPreparationPolicy.ChannelId, SourceId(messageId),
                    reference.SubjectUserId, culture, clock.GetUtcNow().UtcDateTime), ct);
                return other == StructuredDocumentChoiceState.Unavailable ? Result.Success<WhatsAppAssistantTerminalResponse?>(null)
                    : new WhatsAppAssistantTerminalResponse(proposalId, Text("DocumentOtherChosen", culture), culture);
            case StructuredDocumentReplyKind.Duplicate when registration.Duplicate is { } duplicate:
                return new WhatsAppAssistantTerminalResponse(proposalId,
                    Text("SpreadsheetDuplicate", culture, WhatsAppAssistantDocumentPolicy.Date(duplicate.AtUtc, context.TimeZoneId, culture)), culture);
            case StructuredDocumentReplyKind.Quota:
                return new WhatsAppAssistantTerminalResponse(proposalId, Text("DocumentQuota", culture), culture);
            default:
                // What the inbox could not register is not said.
                return Result.Success<WhatsAppAssistantTerminalResponse?>(null);
        }
    }

    /// <summary>
    /// VC1-A (rule B7): over the day's quota a spreadsheet to load is not interpreted; it goes to the inbox unclassified, outside any transaction,
    /// and the person is told with the documents' text, which answers no proposal. Registering it again answers the same while nothing answered
    /// it; what the inbox did not take is not said.
    /// </summary>
    public async Task<Result<WhatsAppAssistantTerminalResponse?>> RegisterOverQuotaAsync(WhatsAppAssistantReferenceRow reference, WhatsAppAssistantSourceRow source,
        CancellationToken ct)
    {
        if (reference.AttachmentMessageId is not { } messageId || source.Spreadsheet is not { } sheet || sheet.MessageId != messageId
            || Prepared(sheet) is not { DataRowCount: not null } prepared) return WhatsAppAssistantErrors.InvalidProposal;
        var culture = WhatsAppAssistantSuggestionPolicy.Context(reference).Culture;
        var registration = await rows.RegisterRowsAsync(Request(reference, source, sheet, prepared, culture, null, ReplyExpiresAtUtc(reference))
            with { NotRead = StructuredDocumentNotRead.Quota }, ct);
        return registration.Kind != StructuredDocumentReplyKind.Quota || await AnsweredAsync(reference, messageId, registration, ct)
            ? Result.Success<WhatsAppAssistantTerminalResponse?>(null)
            : new WhatsAppAssistantTerminalResponse(reference.RequestId, Text("DocumentQuota", culture), culture);
    }

    /// <summary>
    /// A tap on the question of a spreadsheet none of whose rows fit: «Otro» leaves it for the team, as a document's. Null when the question's
    /// file is not a spreadsheet to load. A form chosen from the list cannot be sent yet: the rows' port registers a spreadsheet once and takes
    /// no form fixed by its person, so it is said that it could not be prepared and the question stays open.
    /// </summary>
    public async Task<WhatsAppAssistantDocumentChoice?> ChooseAsync(WhatsAppAssistantReferenceRow reference, Guid proposalId, Guid? definitionId,
        AssistantChannelContext context, CancellationToken ct)
    {
        var culture = context.Culture;
        if (reference.AttachmentMessageId is not { } messageId
            || await reader.FindSourceAsync(reference.ContactId, reference.SourceMessageId, ct) is not { Spreadsheet: { } sheet } source
            || sheet.MessageId != messageId || Prepared(sheet) is not { DataRowCount: not null } prepared) return null;
        var operation = await operations.GetAsync(context, reference.RequestId, ct);
        // A spreadsheet alone that closed without a proposal asked with its reference's own request.
        var proposal = operation.IsSuccess ? operation.Value?.Proposal : null;
        if (definitionId is not null || operation.IsFailure || operation.Value is null
            || (proposal is null ? proposalId != reference.RequestId : proposal.Id != proposalId || !Handles(proposal.Capability)))
            return new(reference, NotPrepared(proposalId, culture), null);
        var registration = await rows.RegisterRowsAsync(Request(reference, source, sheet, prepared, culture, proposal, ReplyExpiresAtUtc(reference)), ct);
        if (registration is not { Kind: StructuredDocumentReplyKind.Unknown, DocumentId: { } documentId }) return new(reference, NotPrepared(proposalId, culture), null);
        var chosen = await documents.ChooseOtherAsync(new(documentId, WhatsAppReceivedDocumentPreparationPolicy.ChannelId, SourceId(messageId),
            reference.SubjectUserId, culture, clock.GetUtcNow().UtcDateTime), ct);
        return chosen == StructuredDocumentChoiceState.Unavailable ? new(reference, NotPrepared(proposalId, culture), null)
            : new(reference, new(proposalId, Text("DocumentOtherChosen", culture), culture), null) { Applies = true };
    }

    /// <summary>
    /// The notice of the loads (VC1-A, rule B2): the rows and their form (only how many with rows of several forms), what goes (each one, the
    /// first ones or the only one), each repeated row with where it is (rule B4: when and where it was loaded, or the row of the spreadsheet it
    /// is equal to) and, in one sentence, what went to the team: past the limit, not fitting or not sendable. Within a WhatsApp text.
    /// </summary>
    internal static string Notice(StructuredDocumentRowsRegistration registration, string timeZoneId, string culture)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var all = registration.Rows;
        var forms = all.Where(row => row.Form is not null).Select(row => row.Form!).DistinctBy(form => form.DefinitionId).ToArray();
        var one = all.Count == 1;
        var sentences = new List<string>
        {
            forms.Length == 1 ? Text(one ? "SpreadsheetNoticeFormOne" : "SpreadsheetNoticeForm", culture, all.Count, forms[0].Name)
                : Text(one ? "SpreadsheetNoticeOne" : "SpreadsheetNotice", culture, all.Count),
        };
        var loads = registration.Loads;
        if (loads == 1) sentences.Add(Text("SpreadsheetLoadsOne", culture));
        else if (loads > 1) sentences.Add(all.Any(row => row.Kind == StructuredDocumentRowKind.OverLimit)
            ? Text("SpreadsheetLoadsFirst", culture, loads) : Text("SpreadsheetLoadsAll", culture));
        foreach (var row in all.Where(row => row.Kind == StructuredDocumentRowKind.Repeated))
            sentences.Add(row.SameAs is { } same ? Text("SpreadsheetRepeatedSame", culture, Label(row.Sheet, row.Number), Label(same.Sheet, same.Number))
                : row.Duplicate is { RecordNumber: { Length: > 0 } number } duplicate
                    ? Text("SpreadsheetRepeated", culture, Label(row.Sheet, row.Number), WhatsAppAssistantDocumentPolicy.Date(duplicate.AtUtc, timeZoneId, culture), number)
                : Text("SpreadsheetRepeatedOne", culture, Label(row.Sheet, row.Number)));
        var team = all.Count(row => ForTeam.Contains(row.Kind));
        if (team == 1) sentences.Add(Text("SpreadsheetForTeamOne", culture));
        else if (team > 1) sentences.Add(Text("SpreadsheetForTeam", culture, team));
        return Fit(string.Join(" ", sentences), WhatsAppMessage.MaxBodyLength);
    }

    /// <summary>The footer of a load, its form and «prellenado» within Meta's 60 characters: a long name is cut with «…».</summary>
    internal static string Footer(string formName, string culture)
    {
        ArgumentNullException.ThrowIfNull(formName);
        var footer = Text("SpreadsheetLoadFooter", culture, formName);
        return footer.Length <= WhatsAppOutgoingMessage.MaxFooterLength ? footer
            : Text("SpreadsheetLoadFooter", culture, Fit(formName, formName.Length - (footer.Length - WhatsAppOutgoingMessage.MaxFooterLength)));
    }

    // Each load in the order of the spreadsheet, its form prepared by formularios with its row: it stays with the person until the inbox stops
    // waiting for them. A load the inbox cannot prepare now, or whose form cannot be resolved for the person, does not go: what is said (the
    // notice, «Por qué lo propuso» and «i de k») follows the loads built and that row goes in the team's sentence; the inbox keeps it for the
    // team after its deadline.
    private async Task<(IReadOnlyList<WhatsAppAssistantSpreadsheetLoad> Loads, StructuredDocumentRowsRegistration Said)> LoadsAsync(
        WhatsAppAssistantReferenceRow reference, WhatsAppAssistantSpreadsheetRow sheet, StructuredSpreadsheet prepared, StructuredDocumentRowsRegistration registration,
        StructuredRecordAssistantProposal recognized, string culture, DateTime deadline, CancellationToken ct)
    {
        var fileName = WhatsAppMessage.SpreadsheetName(sheet.FileName, prepared.MediaType);
        var built = new List<(StructuredDocumentRowRegistration Row, WhatsAppAssistantDocumentSend Send)>();
        foreach (var row in registration.Rows.Where(row => row is { Kind: StructuredDocumentRowKind.Load, Form: not null }).OrderBy(row => row.Position)
                     .Take(WhatsAppAssistantRequest.MaxLoads))
        {
            var ready = await documents.PrepareSendAsync(new(row.DocumentId, WhatsAppReceivedDocumentPreparationPolicy.ChannelId, SourceId(sheet.MessageId),
                reference.SubjectUserId, culture, row.Form!.DefinitionId), ct);
            if (ready is not { State: StructuredDocumentSendState.Ready, Prefill: { } prefill }) continue;
            var send = await sending.ResolveProposedAsync(reference.ContactId, prefill, null, ct);
            if (send.IsFailure) continue;
            // The spreadsheet is not the receipt: the server adds no file on capture.
            built.Add((row, new(row.DocumentId, send.Value, prefill, ready.Marks, null, recognized,
                Text("SpreadsheetReason", culture, fileName, Label(row.Sheet, row.Number)), deadline)));
        }
        var loads = built.Select((item, index) => new WhatsAppAssistantSpreadsheetLoad(item.Send,
            Fit(Text("SpreadsheetLoadBody", culture, index + 1, built.Count, item.Row.Summary.Count > 0 ? string.Join(" · ", item.Row.Summary) : item.Row.Form!.Name),
                MaximumBody), Footer(item.Row.Form!.Name, culture))).ToArray();
        return (loads, Said(registration, [.. built.Select(item => item.Row.DocumentId)]));
    }

    // What is said of the rows: the loads built, numbered in their order, and a load that does not go is one the team takes.
    private static StructuredDocumentRowsRegistration Said(StructuredDocumentRowsRegistration registration, IReadOnlyList<Guid> built) => registration with
    {
        Rows = [.. registration.Rows.Select(row => row.Kind != StructuredDocumentRowKind.Load ? row
            : built.Contains(row.DocumentId) ? row with { Position = built.ToList().IndexOf(row.DocumentId) + 1 }
            : row with { Kind = StructuredDocumentRowKind.NotSendable, Position = null })],
    };

    // Answered from what the inbox kept, a spreadsheet goes only while no reference that carries it was answered.
    private async Task<bool> AnsweredAsync(WhatsAppAssistantReferenceRow reference, Guid messageId, StructuredDocumentRowsRegistration registration,
        CancellationToken ct) =>
        registration.WasAlreadyRegistered && await reader.ExistsAttachmentAnswerAsync(reference.ContactId, messageId, ct);

    private static StructuredDocumentRowsRegistrationRequest Request(WhatsAppAssistantReferenceRow reference, WhatsAppAssistantSourceRow source,
        WhatsAppAssistantSpreadsheetRow sheet, StructuredSpreadsheet prepared, string culture, AssistantObservedProposal? proposal, DateTime expiresAtUtc) =>
        new(WhatsAppReceivedDocumentPreparationPolicy.ChannelId, SourceId(sheet.MessageId), reference.SubjectUserId, culture, source.OccurredAtUtc,
            new StructuredDocumentCopy(sheet.StorageKey, WhatsAppMessage.SpreadsheetName(sheet.FileName, prepared.MediaType), prepared.MediaType, sheet.Size, sheet.Sha256, null))
        {
            Proposal = proposal, SampleSha256 = prepared.Sha256, ReplyExpiresAtUtc = expiresAtUtc,
        };

    // The reply deadline is the expiry of the sends that may follow: the session lifetime, bounded by the reference's content.
    private DateTime ReplyExpiresAtUtc(WhatsAppAssistantReferenceRow reference)
    {
        var expiresAtUtc = clock.GetUtcNow().UtcDateTime.Add(environment.SessionLifetime);
        return reference.ContentExpiresAtUtc < expiresAtUtc ? reference.ContentExpiresAtUtc : expiresAtUtc;
    }

    private static StructuredSpreadsheet? Prepared(WhatsAppAssistantSpreadsheetRow sheet)
    {
        try { return JsonSerializer.Deserialize<StructuredSpreadsheet>(sheet.PreparedJson, JsonSerializerOptions.Web); }
        catch (JsonException) { return null; }
    }

    private static WhatsAppAssistantTerminalResponse NotPrepared(Guid proposalId, string culture) => new(proposalId, Text("DocumentNotPrepared", culture), culture);

    private static string SourceId(Guid messageId) => messageId.ToString("N");

    // A row named as the texts name it: its number, and its sheet when the book has more than one. A number of a row is an identifier.
    private static string Label(string? sheet, int number) =>
        sheet is { Length: > 0 } name ? string.Create(CultureInfo.InvariantCulture, $"{number} ({name})") : number.ToString(CultureInfo.InvariantCulture);

    private static string Text(string key, string culture, params object[] values) => WhatsAppAssistantDocumentPolicy.Text(key, culture, values);

    private static string Fit(string text, int maximum)
    {
        if (text.Length <= maximum) return text;
        var cut = Math.Max(1, maximum - 1);
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut].TrimEnd() + "…";
    }
}
