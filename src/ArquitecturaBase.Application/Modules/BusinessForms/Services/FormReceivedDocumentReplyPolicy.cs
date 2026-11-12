using System.Globalization;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// What formularios decides about a document it registers and what the channel says (decision 11): over the day's quota, unclassified
/// and not read; a file that cannot be opened, discarded with its reason; a proposal that is not authentic or recognizes nothing,
/// unclassified (or discarded when nothing could be read); one whose photo is not legible and lacks a required datum, discarded asking
/// for it again; a repetition, discarded with where it is; «sure», proposed, or proposed for the team when the form cannot be sent to the
/// person (CE4-A); «doubt», proposed with its candidates. The texts are the channel's: this gives their data, in the person's language.
/// </summary>
internal sealed partial class FormReceivedDocumentReplyPolicy(IFormReceivedDocumentReader reader, FormDocumentProposalPolicy proposals,
    FormCatalogOptionsPolicy options, FormReceivedDocumentContentPolicy contents)
{
    public const int MaxForms = 9;
    public const string Quota = "quota";
    public const string Unknown = "unknown";
    public const string Unreadable = "unreadable";
    public const string Duplicate = "duplicate";

    public async Task<FormReceivedDocumentClassification> ClassifyAsync(StructuredDocumentRegistrationRequest request, CancellationToken ct)
    {
        var culture = request.Culture;
        if (request.NotRead == StructuredDocumentNotRead.Quota)
            return new(FormReceivedDocumentState.Unclassified, Quota, null, false, new(null, StructuredDocumentReplyKind.Quota));
        if (request.NotRead == StructuredDocumentNotRead.Rejected || request.Reading is not { } reading)
            return new(FormReceivedDocumentState.Discarded, RejectionCode(request.Rejection), null, false, new(null, StructuredDocumentReplyKind.Rejected));
        FormDocumentPreparedProposal? prepared = null;
        if (request is { Proposal: { } proposal, Copy: { } copy } && FormDocumentObservationPolicy.Authentic(proposal, out var entry, out var read)
            && (read is null || read.Capability == BusinessFormDocumentAssistantCapabilitySource.CandidatesId
                && read.Read.Payload.UserId == request.SenderUserId && read.Read.Payload.Culture == culture))
            prepared = await proposals.PrepareAsync(entry, copy.Sha256, null, ct);
        return await ClassifyAsync(prepared, reading, request.SenderUserId, culture, ct);
    }

    /// <summary>The reply of the document of this channel source, when it is registered (another sender's is not theirs to hear about).</summary>
    public async Task<StructuredDocumentRegistration?> FindReplayAsync(StructuredDocumentRegistrationRequest request, CancellationToken ct) =>
        await reader.FindBySourceAsync(request.ChannelId, request.SourceId, ct) is not { } row ? null
            : row.SenderUserId == request.SenderUserId ? await ReplayAsync(row, request.Culture, ct)
            : new StructuredDocumentRegistration(null, StructuredDocumentReplyKind.Unavailable);

    /// <summary>
    /// The reply of a document already registered, from what it kept and without classifying it again: its state, its reason and its stored
    /// proposal answer the same, with the names its forms have now. A confirmed one answers as it was proposed.
    /// </summary>
    public async Task<StructuredDocumentRegistration> ReplayAsync(FormReceivedDocumentStoredRow row, string culture, CancellationToken ct)
    {
        var proposal = contents.ReadProposal(row.Id, row.ProtectedProposal);
        var reading = contents.ReadReading(row.Id, row.ProtectedReading);
        Dictionary<Guid, FormDocumentVersionRow> versions = proposal is { Candidates.Count: > 0 }
            ? (await reader.ListVersionsAsync(proposal.Candidates.Select(candidate => candidate.VersionId).Distinct().ToArray(), ct)).ToDictionary(version => version.VersionId)
            : new Dictionary<Guid, FormDocumentVersionRow>();
        StructuredDocumentRegistration reply;
        if (row.State == FormReceivedDocumentState.Unclassified)
            reply = row.ReasonCode == Quota ? new(row.Id, StructuredDocumentReplyKind.Quota)
                : new(row.Id, StructuredDocumentReplyKind.Unknown) { Forms = await FormsAsync(culture, ct) };
        else if (row is { State: FormReceivedDocumentState.Discarded, ReasonCode: Duplicate } && proposal?.Repetition is { } repetition)
            reply = new(row.Id, StructuredDocumentReplyKind.Duplicate) { Duplicate = new(repetition.AtUtc, repetition.RecordNumber) };
        else if (row is { State: FormReceivedDocumentState.Discarded, ReasonCode: Unreadable } && reading is not null)
            reply = new(row.Id, StructuredDocumentReplyKind.Unreadable)
            {
                MissingDatum = proposal is { Candidates: [var read, ..] } && versions.TryGetValue(read.VersionId, out var readVersion)
                    ? MissingRequired(read, FormDraftPolicy.Read<FormVersionSchema>(readVersion.SchemaJson), culture) : null,
                Problem = reading.Problem,
            };
        else if (row.State is FormReceivedDocumentState.Proposed or FormReceivedDocumentState.Confirmed && proposal is { Candidates.Count: > 0 })
        {
            if (row.State == FormReceivedDocumentState.Proposed && proposal.Outcome == FormDocumentEntryProposal.Doubt)
                reply = new(row.Id, StructuredDocumentReplyKind.Doubt)
                {
                    Forms = proposal.Candidates.Select(candidate => Choice(candidate, versions, culture)).OfType<StructuredDocumentFormChoice>().ToArray(),
                };
            else
            {
                var candidate = proposal.Candidates.FirstOrDefault(value => value.DefinitionId == row.DefinitionId) ?? proposal.Candidates[0];
                var form = await reader.FindActiveFormAsync(candidate.DefinitionId, ct);
                reply = new(row.Id, row.NotSendable ? StructuredDocumentReplyKind.NotSendable : StructuredDocumentReplyKind.Sure)
                {
                    Forms = Choice(candidate, versions, culture) is { } choice ? [choice] : [],
                    Summary = form is not null && form.VersionId == candidate.VersionId ? Summary(form, candidate, culture) : null,
                };
            }
        }
        else reply = new(row.Id, StructuredDocumentReplyKind.Rejected);
        return reply with { DocumentId = row.Id, WasAlreadyRegistered = true };
    }

    // A candidate of the stored proposal by the name of the version it was read for.
    private static StructuredDocumentFormChoice? Choice(FormDocumentPreparedCandidate candidate, Dictionary<Guid, FormDocumentVersionRow> versions,
        string culture) =>
        versions.TryGetValue(candidate.VersionId, out var version)
            ? new(candidate.DefinitionId, candidate.VersionId,
                FormAssistantCandidatePolicy.Text(FormDraftPolicy.Read<Dictionary<string, string>>(version.NameJson), culture, version.Culture))
            : null;

    private async Task<FormReceivedDocumentClassification> ClassifyAsync(FormDocumentPreparedProposal? prepared, StructuredDocumentReading reading,
        Guid sender, string culture, CancellationToken ct)
    {
        var legible = reading.Readability == StructuredDocumentReadability.Readable;
        if (prepared is not { Outcome: not FormDocumentEntryProposal.Unknown, Candidates.Count: > 0 })
            return legible
                ? new(FormReceivedDocumentState.Unclassified, Unknown, prepared, false,
                    new(null, StructuredDocumentReplyKind.Unknown) { Forms = await FormsAsync(culture, ct) })
                : new(FormReceivedDocumentState.Discarded, Unreadable, prepared, false,
                    new(null, StructuredDocumentReplyKind.Unreadable) { Problem = reading.Problem });
        var first = prepared.Candidates[0];
        var row = await reader.FindActiveFormAsync(first.DefinitionId, ct);
        if (row is null || row.VersionId != first.VersionId)
            return new(FormReceivedDocumentState.Unclassified, Unknown, prepared, false,
                new(null, StructuredDocumentReplyKind.Unknown) { Forms = await FormsAsync(culture, ct) });
        var schema = FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson);
        if (!legible && MissingRequired(first, schema, culture) is { } missing)
            return new(FormReceivedDocumentState.Discarded, Unreadable, prepared, false,
                new(null, StructuredDocumentReplyKind.Unreadable) { MissingDatum = missing, Problem = reading.Problem });
        if (prepared.Repetition is { } repetition)
            return new(FormReceivedDocumentState.Discarded, Duplicate, prepared, false,
                new(null, StructuredDocumentReplyKind.Duplicate) { Duplicate = new(repetition.AtUtc, repetition.RecordNumber) });
        if (prepared.Outcome == FormDocumentEntryProposal.Doubt)
        {
            var forms = new List<StructuredDocumentFormChoice>();
            foreach (var candidate in prepared.Candidates)
                if (await reader.FindActiveFormAsync(candidate.DefinitionId, ct) is { } form)
                    forms.Add(new(form.DefinitionId, form.VersionId, FormDocumentFormPolicy.Name(form, culture)));
            return new(FormReceivedDocumentState.Proposed, null, prepared, false, new(null, StructuredDocumentReplyKind.Doubt) { Forms = forms });
        }
        var sendable = (await options.PrepareAsync(sender, culture, schema, row.Culture, ct, inspectOnly: true)).CanSend;
        var choice = new StructuredDocumentFormChoice(row.DefinitionId, row.VersionId, FormDocumentFormPolicy.Name(row, culture));
        return new(FormReceivedDocumentState.Proposed, null, prepared, !sendable,
            new(null, sendable ? StructuredDocumentReplyKind.Sure : StructuredDocumentReplyKind.NotSendable)
            {
                Forms = [choice], Summary = Summary(row, first, culture),
            });
    }

    // The forms with reading on the person can complete, by name: the list of «no lo reconoce».
    private async Task<IReadOnlyList<StructuredDocumentFormChoice>> FormsAsync(string culture, CancellationToken ct) =>
        (await reader.ListReadingFormsAsync(ct)).Where(row => !row.AppOnly && FormDocumentFormPolicy.Reading(row) is { Enabled: true })
            .Select(row => new StructuredDocumentFormChoice(row.DefinitionId, row.VersionId, FormDocumentFormPolicy.Name(row, culture)))
            .OrderBy(choice => choice.Name, StringComparer.Create(CultureInfo.GetCultureInfo(culture), true)).Take(MaxForms).ToArray();

    private static string? MissingRequired(FormDocumentPreparedCandidate candidate, FormVersionSchema schema, string culture) =>
        candidate.Values.Where(value => value.Value is null).Select(value => schema.Questions.SingleOrDefault(question => question.FieldId == value.FieldId))
            .FirstOrDefault(question => question is { Required: true }) is { } missing ? FormRecordPresentationPolicy.Text(missing.Label, culture) : null;

    // The standard's summary for WhatsApp in the person's language, with each datum it names by its key in braces; none when a datum is missing.
    private string? Summary(FormDocumentFormRow row, FormDocumentPreparedCandidate candidate, string culture)
    {
        var standard = proposals.Standard(row);
        if (standard?.Reading?.Summary is not { } summaries || FormDocumentProposalPolicy.Bindings(row) is not { } bound
            || !summaries.TryGetValue(culture, out var template)) return null;
        var complete = true;
        var text = Placeholder().Replace(template, match =>
        {
            var value = bound.TryGetValue(match.Groups[1].Value, out var fieldId) ? candidate.Values.SingleOrDefault(item => item.FieldId == fieldId) : null;
            if (value is not { Value: not null } || Display(value, culture) is not { } shown) { complete = false; return ""; }
            return shown;
        });
        return complete ? text : null;
    }

    /// <summary>A value as the person reads it: amounts with the group separator of the language, dates only with numbers, an option by its text.</summary>
    public static string? Display(FormDocumentPreparedValue value, string culture)
    {
        if (value.Value is not { } text) return null;
        return value.Type switch
        {
            "decimal" => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                ? DisplayNumbers.Number(number, culture, maximumDecimals: 2) : text,
            "date" => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToString(culture == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy", CultureInfo.InvariantCulture) : text,
            "boolean" => FormResponseTexts.Get(text == "true" ? "true" : "false", culture),
            "choice" => value.Text is { } texts ? texts.TryGetValue(culture, out var shown) ? shown : texts.Values.FirstOrDefault() : null,
            _ => text,
        };
    }

    private static string RejectionCode(StructuredAttachmentRejection? rejection) => rejection switch
    {
        StructuredAttachmentRejection.Legacy => "legacy",
        StructuredAttachmentRejection.Protected => "protected",
        StructuredAttachmentRejection.MacroEnabled => "macroEnabled",
        StructuredAttachmentRejection.TooLarge => "tooLarge",
        StructuredAttachmentRejection.TooManyPages => "tooManyPages",
        StructuredAttachmentRejection.Empty => "empty",
        _ => Unreadable,
    };

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9]*)\}", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Placeholder();
}

/// <summary>What formularios decided about a document it registers: its state and reason, its prepared proposal and what to tell the person.</summary>
internal sealed record FormReceivedDocumentClassification(FormReceivedDocumentState State, string? ReasonCode, FormDocumentPreparedProposal? Proposal,
    bool NotSendable, StructuredDocumentRegistration Reply)
{
    public override string ToString() => "FormReceivedDocumentClassification { Content = [REDACTED] }";
}
