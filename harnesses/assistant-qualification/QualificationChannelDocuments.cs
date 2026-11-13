using System.Globalization;
using System.Resources;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace AssistantQualification;

// channel-documents (task 17a): what the app does with the proposal of a case. A document: «sure» (the form it proposes), «doubt» (the
// question with its two forms), «unknown» («No lo reconoce» with the list) or «unreadable» («No se lee», discarded), with the manifest facts
// each candidate keeps in a question of its form (fact id → question key). A spreadsheet: «rows», each form with its rows and the column of
// each question (Dubious: a column that, if it is mapped, goes to that question read with doubts), or «unknown» when no row fits; Note, the
// untrusted cell text the model reads whole.
internal sealed record ChannelLoadingExpectation(string Decision, string[] Forms, IReadOnlyDictionary<string, string>? Facts = null,
    ChannelRowsExpectation[]? Rows = null, string? Note = null);
internal sealed record ChannelRowsExpectation(string Form, int[] Rows, IReadOnlyDictionary<string, string> Columns,
    IReadOnlyDictionary<string, string>? Dubious = null);
// The case file names the exact manifest and document-reading cases it was written for, so the evidence (which hashes it) covers them.
internal sealed record ChannelDocumentSet(string ManifestSha256, string ReadingsSha256, ChannelCase[] Cases);
internal sealed record VerifiedChannelDocumentSet(ChannelCase[] Cases, DocumentManifest Manifest, DocumentSet Readings)
{
    public DocumentFixture Document(ChannelCase testCase) => Manifest.Documents.Single(document => document.FileName == testCase.Document!.FileName);
    // A channel reading expects what document-reading expects of the same file in the same culture: same candidate, same instructions.
    public DocumentCase Reading(ChannelCase testCase) =>
        Readings.Cases.Single(reading => reading.FileName == testCase.Document!.FileName && reading.Culture == testCase.Culture);
}
internal sealed record ReadingFormField(string Key, string Type, string Spanish, string English, int? MaxLength, bool Required);
internal sealed record ReadingForm(string Alias, string Recognition, ReadingFormField[] Fields);

internal static class QualificationChannelDocuments
{
    public const string CaseFile = "channel-document-cases.json";
    public const string Loader = "loader";
    public const string SheetLoader = "sheet-loader";
    public const string ReadingOperation = "whatsapp-document";
    public const string Candidates = "forms.documents.candidates";
    public const string ProposeEntry = "forms.documents.propose-entry";
    public const string ProposeRows = "forms.documents.propose-rows";
    public static readonly string[] EntryDecisions = ["sure", "doubt", "unknown", "unreadable"];
    public static readonly string[] RowsDecisions = ["rows", "unknown"];
    public static readonly string[] LoadingKinds = ["loader", "loader-alone", "sheet-loader", "sheet-loader-alone", "sheet-loader-focused"];

    // WhatsAppAssistantAccessPolicy.DocumentCapabilities and SpreadsheetCapabilities, and the three that register with text, which a turn
    // that loads a spreadsheet swaps for the loading ones.
    public static readonly AssistantCapabilityReference[] DocumentCapabilities = [new(Candidates, 1), new(ProposeEntry, 1)];
    public static readonly AssistantCapabilityReference[] SpreadsheetCapabilities = [new(Candidates, 1), new(ProposeRows, 1)];
    private static readonly AssistantCapabilityReference[] TextRegistration =
        [new("forms.assistant-candidates", 1), new("forms.propose-registration", 1), new("forms.clarify-registration", 1)];
    // What the engine leaves out of a turn by its file (each definition's Attachment): with a document, what needs a spreadsheet and what
    // never goes with a document; with a spreadsheet, what needs a document.
    private static readonly AssistantCapabilityReference[] SpreadsheetOnly = [new(SpreadsheetOracle.ProposeImport, 1), new(ProposeRows, 1)];
    private static readonly AssistantCapabilityReference[] NoDocument = [..TextRegistration, new("forms.missing-request.propose", 1)];
    private static readonly AssistantCapabilityReference[] DocumentOnly = [new(ProposeEntry, 1)];
    public const int MaximumDefinitions = 16; // AiAssistantCapabilityCatalogPolicy.MaximumDefinitions
    // Everything a context of the set may offer, by Id and version: the stamps of its evidence.
    public static readonly AssistantCapabilityReference[] References =
        [..QualificationChannelContexts.AutomaticReferences.Concat(DocumentCapabilities).Concat(SpreadsheetCapabilities).Distinct()];

    // The forms with document reading the set publishes (QualificationFormSeed.CreateReadingFormsAsync): two that read transfers, told apart
    // only by who paid whom, so a receipt alone is a doubt and the request decides it. Their «Cómo reconocerlo» list the same printed data
    // (every receipt prints the CUIT and the CBU or CVU), so only the direction tells them apart.
    public static readonly QualificationFormName[] FormNames = [new("collection", "COB", "Cobranzas", "Collections"),
        new("supplier-payment", "PAG", "Pagos a proveedores", "Supplier payments")];
    public static string RecognitionData(string party) => ": dice la fecha, " + party + ", el monto, la CUIT, la CBU o CVU y el número de operación.";
    private static readonly ReadingFormField Date = new("date", "date", "Fecha", "Date", null, true);
    private static readonly ReadingFormField Amount = new("amount", "decimal", "Monto", "Amount", null, true);
    private static readonly ReadingFormField Operation = new("operation", "shortText", "Número de operación", "Operation number", 40, false);
    public static readonly ReadingForm[] ReadingForms =
    [
        new("collection", "Comprobante de una transferencia que nos hizo un cliente para pagarnos, o una planilla de cobros a clientes"
            + RecognitionData("el cliente"),
            [Date, Amount, new("customer", "shortText", "Cliente", "Customer", 80, false), new("cuit", "shortText", "CUIT del cliente", "Customer CUIT", 13, false),
                new("account", "shortText", "CBU o CVU", "CBU or CVU", 22, false), Operation]),
        new("supplier-payment", "Comprobante de una transferencia que hicimos para pagarle a un proveedor, o una planilla de pagos a proveedores"
            + RecognitionData("el proveedor"),
            [Date, Amount, new("supplier", "shortText", "Proveedor", "Supplier", 80, false), new("cuit", "shortText", "CUIT del proveedor", "Supplier CUIT", 13, false),
                new("account", "shortText", "CBU o CVU de destino", "Destination CBU or CVU", 22, false), Operation]),
    ];
    public static ReadingForm? ReadingFormOf(string alias) => ReadingForms.SingleOrDefault(form => form.Alias == alias);

    public static bool IsDocument(ChannelCase testCase) => testCase.ActorKind == Loader;

    // The context the channel builds for the case: with its request, alone, or (VC1-A, task 29b) the form chosen from the list.
    public static string Kind(ChannelCase testCase) => testCase.ActorKind == Loader
        ? testCase.Question.Length > 0 || testCase.Audio is not null ? "loader" : "loader-alone"
        : testCase.Focus is not null ? "sheet-loader-focused" : testCase.Question.Length > 0 ? "sheet-loader" : "sheet-loader-alone";

    // A copy of WhatsAppAssistantAccessPolicy.WithDocument, ForDocument, WithSpreadsheet, ForSpreadsheet and Focused, on the person's automatic
    // context: the policy is internal to Application and the harness never reaches it by reflection, so an offline control holds the copy to the
    // app's definition counts (WhatsAppAssistantFileCatalogTests) and the oracle compares the sealed turn with it.
    public static AssistantChannelContext Context(AssistantChannelContext automatic, string kind, Guid? focus)
    {
        if ((kind == "sheet-loader-focused") != focus.HasValue || focus == Guid.Empty) throw new ArgumentException("Only a chosen form fixes its focus.", nameof(focus));
        AssistantChannelContext Alone(AssistantCapabilityReference[] capabilities) => automatic with
            { PreloadedCapabilities = null, AllowedTerminals = [new("assistant_proposal", 1)], AllowedCapabilities = capabilities };
        return kind switch
        {
            "loader" => automatic with { AllowedCapabilities = [..automatic.AllowedCapabilities.Where(item => !DocumentCapabilities.Contains(item)), ..DocumentCapabilities] },
            "loader-alone" => Alone(DocumentCapabilities),
            "sheet-loader" => automatic with
            {
                PreloadedCapabilities = automatic.PreloadedCapabilities?.Where(item => !TextRegistration.Contains(item)).ToArray(),
                AllowedCapabilities = [..automatic.AllowedCapabilities.Where(item => !TextRegistration.Contains(item) && !SpreadsheetCapabilities.Contains(item)),
                    ..SpreadsheetCapabilities],
            },
            "sheet-loader-alone" => Alone(SpreadsheetCapabilities),
            "sheet-loader-focused" => Alone(SpreadsheetCapabilities) with { FocusDefinitionId = focus },
            _ => throw new ArgumentException("Unknown loading kind.", nameof(kind)),
        };
    }

    // A copy of WhatsAppAssistantAccessPolicy.LoadsDocumentOnly and LoadsSpreadsheetOnly: a sealed context that only loads the file (ForDocument,
    // ForSpreadsheet or Focused), the only one whose turn without a proposal still goes to the inbox (WhatsAppAssistantTerminalPolicy.
    // HandlesDocumentWithoutProposal) and gets «No lo reconoce» or «No se lee».
    public static bool LoadsOnly(AssistantChannelContext context, bool document)
    {
        var only = document ? DocumentCapabilities : SpreadsheetCapabilities;
        return context.AllowedCapabilities.Count == only.Length && context.AllowedCapabilities.All(only.Contains);
    }

    // What the engine offers of a sealed context in a turn with this file, before the permission filter, plus the terminal.
    public static int Definitions(AssistantChannelContext context, bool document) => context.AllowedCapabilities.Count(item => document
        ? !SpreadsheetOnly.Contains(item) && !NoDocument.Contains(item) : !DocumentOnly.Contains(item)) + context.AllowedTerminals.Count;

    // The reading of a document the person sent, as WhatsAppReceivedDocumentReadingPolicy captures it: the person as Actor and Subject in
    // the automatic channel, Assistant, nothing required, offered or allowed to end the turn.
    public static AssistantChannelContext ReadingContext(Guid personUserId, string culture) => new("whatsapp-assistant", personUserId, personUserId, culture,
        "America/Argentina/Buenos_Aires", new([], []), [], []) { InteractionMode = AssistantInteractionMode.Assistant };

    public static bool IsReadingLedger(QualificationLedgerEntry entry) => entry is { OperationKind: ReadingOperation, ChannelId: "whatsapp-assistant",
            ChannelKind: null, SourceRecordId: null, PreviousRequestId: null, FocusDefinitionId: null, InteractionMode: AssistantInteractionMode.Assistant }
        && entry.SubjectUserId == entry.ActorUserId && entry.Culture is "es" or "en"
        && entry.CaseId.StartsWith("channel-document-", StringComparison.Ordinal) && entry.CaseId.EndsWith("-document", StringComparison.Ordinal);

    // The name the app gives the file in the text, the attachment and its document (WhatsAppMessage.DocumentName): a photo from WhatsApp
    // comes without a name and is named by its type («image.jpg», «image.png»); a PDF keeps the one the phone sends.
    public static string ChannelName(string fileName, string mediaType) =>
        WhatsAppMessage.DocumentName(mediaType is "image/jpeg" or "image/png" ? null : fileName, mediaType);

    // A file alone is named by the server (WhatsAppAssistantDispatchService.Work, DocumentAlone or SpreadsheetAlone): a channel turn always
    // says something.
    public static string AloneText(string resource, string culture, string fileName)
    {
        if (resource is not ("DocumentAlone" or "SpreadsheetAlone")) throw new ArgumentException("Unknown public channel text.", nameof(resource));
        var resources = new ResourceManager("ArquitecturaBase.Application.Modules.WhatsApp.Resources.AssistantTerminalTexts", typeof(AssistantChannelContext).Assembly);
        var text = resources.GetString(resource, CultureInfo.GetCultureInfo(culture)) ?? throw new InvalidOperationException("Missing public channel text.");
        return string.Format(CultureInfo.InvariantCulture, text, fileName);
    }

    // The text the interpretation takes: the server's transcript of a voice note, the server's line for a document or a spreadsheet alone
    // (with the name the app gives it), or what was written (the caption, the contiguous text or the title of the chosen form).
    public static string Text(ChannelCase testCase, string? dictated) => dictated ?? Kind(testCase) switch
    {
        "loader-alone" => AloneText("DocumentAlone", testCase.Culture, ChannelName(testCase.Document!.FileName, testCase.Document.ContentType)),
        "sheet-loader-alone" => AloneText("SpreadsheetAlone", testCase.Culture, testCase.Spreadsheet!.FileName),
        _ => testCase.Question,
    };

    public static bool IsValid(ChannelCase testCase)
    {
        if (testCase is not { Loading: { } loading, Authoring: null, Values: null, FormAlias: null } || testCase.Culture is not ("es" or "en")
            || !testCase.Id.EndsWith("-" + testCase.Culture, StringComparison.Ordinal) || loading.Forms is null) return false;
        var document = testCase.ActorKind == Loader;
        if (!Regex.IsMatch(testCase.Id, document ? "^channel-document-[a-z0-9-]{1,60}-(es|en)$" : "^channel-sheet-[a-z0-9-]{1,60}-(es|en)$", RegexOptions.CultureInvariant)
            || !(document ? testCase is { Document: not null, Spreadsheet: null, ExpectedPayload: "document-entry", Focus: null, Continues: null }
                : testCase is { ActorKind: SheetLoader, Document: null, Spreadsheet: not null, ExpectedPayload: "document-rows", Audio: null }))
            return false;
        if (testCase.Audio is { } audio && (!AudioFixtureRules.IsValidVoiceNote(audio) || testCase.Question.Length != 0)) return false;
        if (testCase.RequiredTools.Any(tool => tool != Candidates)
            || loading.Decision is "sure" or "doubt" or "rows" && !testCase.RequiredTools.Contains(Candidates, StringComparer.Ordinal)) return false;
        if (loading.Forms.Any(alias => ReadingFormOf(alias) is null) || loading.Forms.Distinct(StringComparer.Ordinal).Count() != loading.Forms.Length) return false;
        if (document)
        {
            if (!EntryDecisions.Contains(loading.Decision, StringComparer.Ordinal) || loading.Rows is not null || loading.Note is not null
                || loading.Forms.Length != loading.Decision switch { "sure" => 1, "doubt" => 2, _ => 0 }) return false;
            return (loading.Facts ?? new Dictionary<string, string>()).All(fact =>
                loading.Forms.All(alias => ReadingFormOf(alias)!.Fields.Any(field => field.Key == fact.Value)));
        }
        var sheet = testCase.Spreadsheet!;
        var rows = loading.Rows ?? [];
        if (!RowsDecisions.Contains(loading.Decision, StringComparer.Ordinal) || loading.Facts is not null || !QualificationSpreadsheets.IsValid(sheet)
            || (loading.Decision == "rows") != (rows.Length > 0) || !rows.Select(item => item.Form).SequenceEqual(loading.Forms)
            || sheet.Inherited != testCase.Continues is not null || (testCase.Focus is not null) != (testCase.Continues is not null)
            || testCase.Focus is { } focus && (ReadingFormOf(focus) is null || loading.Forms.Any(alias => alias != focus))) return false;
        var numbers = rows.SelectMany(item => item.Rows).ToArray();
        return numbers.All(number => number >= 2) && numbers.Distinct().Count() == numbers.Length && rows.All(item => item.Rows.Length > 0
            && ReadingFormOf(item.Form) is { } form && item.Columns.Count > 0
            && item.Columns.Concat(item.Dubious ?? new Dictionary<string, string>()).All(column => Regex.IsMatch(column.Value, "^[A-Z]$", RegexOptions.CultureInvariant)
                && form.Fields.Any(field => field.Key == column.Key))
            && (item.Dubious ?? new Dictionary<string, string>()).Keys.All(key => !item.Columns.ContainsKey(key)));
    }

    // The cases and the manifest agree: each document is a file the provider reads, as the manifest has it, with exactly one document-reading
    // case in its culture and only facts it prints; every case has its twin in the other culture with the same kind of file and the same
    // expectation; a continuation follows its start in the same culture and carries its spreadsheet, and the start is the spreadsheet alone
    // that the app answers with the «No lo reconoce» list.
    public static bool Inventory(ChannelCase[] cases, DocumentManifest manifest, DocumentSet readings)
    {
        if (cases.Length == 0 || !cases.All(IsValid) || cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != cases.Length) return false;
        foreach (var testCase in cases)
        {
            var loading = testCase.Loading!;
            if (testCase.Document is { } document)
            {
                var printed = manifest.Documents.SingleOrDefault(item => item.FileName == document.FileName);
                if (printed is not { Use: "provider" } || printed.MediaType != document.ContentType || printed.ByteCount != document.ByteCount
                    || printed.Sha256 != document.Sha256 || printed.Pages != document.Pages || document.Inherited || document.Expected is not null
                    || !(loading.Facts ?? new Dictionary<string, string>()).Keys.All(printed.Facts.ContainsKey)
                    || readings.Cases.Count(reading => reading.FileName == document.FileName && reading.Culture == testCase.Culture) != 1) return false;
            }
            var twin = cases.SingleOrDefault(item => item.Id == testCase.Id[..^2] + (testCase.Culture == "es" ? "en" : "es"));
            if (twin is null || twin.Document?.FileName != testCase.Document?.FileName || (twin.Audio is null) != (testCase.Audio is null)
                || twin.Loading!.Decision != loading.Decision || !twin.Loading.Forms.SequenceEqual(loading.Forms) || twin.Focus != testCase.Focus
                || (twin.Question.Length == 0) != (testCase.Question.Length == 0)) return false;
            if (testCase.Continues is { } continues)
            {
                var position = Array.IndexOf(cases, testCase);
                var start = cases.Take(position).SingleOrDefault(item => item.Id == continues);
                if (start is null || start.Culture != testCase.Culture || start.Spreadsheet is not { Inherited: false } sheet
                    || sheet.FileName != testCase.Spreadsheet!.FileName || sheet.Sha256 != testCase.Spreadsheet.Sha256 || start.Loading!.Decision != "unknown"
                    || Kind(start) != "sheet-loader-alone") return false;
            }
        }
        return true;
    }

    public static string CasePath => Path.Combine(AppContext.BaseDirectory, "fixtures", CaseFile);

    public static ChannelDocumentSet Load(byte[] bytes) =>
        JsonSerializer.Deserialize<ChannelDocumentSet>(bytes, JsonSerializerOptions.Web) ?? throw new InvalidOperationException("Invalid public channel document cases.");

    public static bool Verified(ChannelDocumentSet file, byte[] manifestBytes, byte[] readingBytes, DocumentSet readings, DocumentManifest manifest) =>
        file is { ManifestSha256: not null, ReadingsSha256: not null, Cases: not null } && file.ManifestSha256 == EvidenceWriter.HashPublicBytes(manifestBytes)
        && file.ReadingsSha256 == QualificationHomeDocuments.ReadingsHash(readingBytes) && QualificationDocuments.Verified(readings, manifest, manifestBytes)
        && Inventory(file.Cases, manifest, readings);

    // Before a run touches its ledger, database or provider, and before a listing: the case file names this exact manifest and these exact
    // document-reading cases, the cases agree with both, and every document, spreadsheet and voice note has the size and SHA-256 pinned.
    public static async Task<VerifiedChannelDocumentSet> LoadVerifiedAsync(CancellationToken cancellationToken)
    {
        var file = Load(await File.ReadAllBytesAsync(CasePath, cancellationToken));
        var manifestBytes = await File.ReadAllBytesAsync(QualificationDocuments.ManifestPath, cancellationToken);
        var readingBytes = await File.ReadAllBytesAsync(QualificationHomeDocuments.ReadingsPath, cancellationToken);
        var readings = QualificationDocuments.LoadSet();
        var manifest = QualificationDocuments.Manifest();
        if (!Verified(file, manifestBytes, readingBytes, readings, manifest)) throw new InvalidOperationException("Channel document fixture inventory changed.");
        var set = new VerifiedChannelDocumentSet(file.Cases, manifest, readings);
        foreach (var testCase in file.Cases)
        {
            if (testCase.Document is { } document) _ = await QualificationHomeDocuments.ReadAsync(document, set.Document(testCase), cancellationToken);
            if (testCase.Spreadsheet is { } sheet) _ = await QualificationSpreadsheets.ReadAsync(sheet, cancellationToken);
            if (testCase.Audio is { } audio)
            {
                var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "audio", audio.FileName), cancellationToken);
                if (bytes.LongLength != audio.ByteCount || EvidenceWriter.HashPublicBytes(bytes) != audio.Sha256 || !AudioFixtureRules.IsVoiceNote(bytes))
                    throw new InvalidOperationException("Public voice note fixture changed.");
            }
        }
        return set;
    }
}
