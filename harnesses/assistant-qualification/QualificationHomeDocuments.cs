using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace AssistantQualification;

// Stage 3 of «La IA arma formularios y catálogos» in Home (home-documents): a fictitious document of fixtures/documents that the turn brings
// or inherits from its conversation. A photo or a PDF goes up first through the real Home document service, which reads it in home-document;
// a Word goes with its turn through the file turn, like a spreadsheet, and the server reads it. The case pins the file as the manifest has it.
internal sealed record HomeDocumentFixture(string FileName, string ContentType, long ByteCount, string Sha256, int? Pages,
    bool Inherited = false, HomeDocumentExpectation? Expected = null);

// What the proposed form does with the document: its reading on (true), off (false) or either (null); the manifest facts each kept by exactly
// one question's sample, in the type its kind takes; every question sampled; the questions the document prints, in their order (a label, the
// types it may take or the options of its own list, each only when pinned); at least so many questions; and, with a hidden instruction, that
// nothing obeys it.
internal sealed record HomeDocumentExpectation(bool? DocumentReading, string[] Facts, bool EverySample = false,
    HomePrintedQuestion[]? Printed = null, int MinimumQuestions = 1, bool Injection = false);
internal sealed record HomePrintedQuestion(string? Label = null, string[]? Types = null, string[]? Options = null, string? Catalog = null);

// The case file names the exact manifest and document-reading cases it was written for, so the evidence (which hashes the case file) covers
// every input of its oracles too.
internal sealed record HomeDocumentSet(string ManifestSha256, string ReadingsSha256, HomeCase[] Cases);

// The cases, the manifest they agree with and the document-reading cases a Home reading expects the same of.
internal sealed record VerifiedHomeDocumentSet(HomeCase[] Cases, DocumentManifest Manifest, DocumentSet Readings)
{
    public DocumentFixture Document(HomeDocumentFixture fixture) => Manifest.Documents.Single(document => document.FileName == fixture.FileName);

    // A Home reading expects what the document-reading set expects of the same file in the same culture: same candidate, same instructions.
    public DocumentCase Reading(HomeCase testCase) =>
        Readings.Cases.Single(reading => reading.FileName == testCase.Document!.FileName && reading.Culture == testCase.Culture);
}

internal static class QualificationHomeDocuments
{
    public const string CaseFile = "home-document-cases.json";
    public const string ChannelId = "home-document";
    public const string WordMediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string DocumentPrefix = "ai/assistant/documents/";
    private const string AttachmentPrefix = "ai/assistant/attachments/";
    // The question types of forms.authoring.propose-form@2 (FormAuthoringAssistantCodec.Document).
    public static readonly string[] QuestionTypes = ["shortText", "longText", "number", "date", "yesNo", "choice", "multiChoice", "catalog", "photo", "document"];

    public static bool IsWord(HomeDocumentFixture document) => document.ContentType == WordMediaType;

    // The kind the bubble says: pdf, jpeg or png for what the provider reads, docx for a Word.
    public static string? Kind(string contentType) => contentType switch
    {
        "application/pdf" => "pdf",
        "image/jpeg" => "jpeg",
        "image/png" => "png",
        WordMediaType => "docx",
        _ => null,
    };

    // The private copy a turn seals: a photo or a PDF under its reading's request (AiAssistantHomeDocumentPolicy.Key), a Word under its own
    // turn's request, like a spreadsheet (AiAssistantHomeAttachmentPolicy.WordKey).
    public static string Key(Guid requestId, string contentType) => contentType == WordMediaType
        ? AttachmentPrefix + requestId.ToString("N") + ".docx"
        : DocumentPrefix + requestId.ToString("N") + (QualificationDocuments.Extension(contentType) ?? throw new InvalidOperationException("Unsupported document type."));

    // The copies a Home turn keeps in the isolated store (QualificationPrivateFiles): a photo or a PDF under the documents prefix, a Word under
    // the attachments one.
    public static bool IsKey(string key) =>
        Regex.IsMatch(key, "^ai/assistant/documents/[a-f0-9]{32}\\.(pdf|jpg|png)$", RegexOptions.CultureInvariant)
        || Regex.IsMatch(key, "^ai/assistant/attachments/[a-f0-9]{32}\\.docx$", RegexOptions.CultureInvariant);

    // The type a fact takes in a question, by its kind (amount-2 is an amount): a date, a number or a short text for an account or a tax key.
    public static string? FactType(string fact) => fact.Split('-')[0] switch
    {
        "date" => "date",
        "amount" or "total" => "number",
        "cbu" or "cuit" => "shortText",
        _ => null,
    };

    // The Home document channel as the server seals it (AiAssistantChannelPolicy.HomeDocument): the person, the culture and the person's time
    // zone, the Assistant required and nothing offered. Reading the operation back needs this same scope.
    public static AssistantChannelContext Context(Guid actorUserId, string culture, string timeZoneId) =>
        new(ChannelId, actorUserId, null, culture, timeZoneId, new([ArtificialIntelligencePermissions.AssistantUse], []), [], []);

    // A ledger of a Home reading names its person, the channel, the culture and nothing else; a scope that says more was not this set's.
    public static bool IsLedger(QualificationLedgerEntry entry) => entry is { OperationKind: "home-document", ChannelId: ChannelId, SubjectUserId: null,
            ChannelKind: null, SourceRecordId: null, PreviousRequestId: null, InteractionMode: AssistantInteractionMode.Unspecified }
        && entry.Culture is "es" or "en" && entry.CaseId.StartsWith("home-document-", StringComparison.Ordinal)
        && entry.CaseId.EndsWith("-document", StringComparison.Ordinal);

    // A case of the set: an author's Home turn with a document, which expects either no proposal or the form in version 2 with what it does
    // with the document; a photo is one page and a Word has none; an inherited document continues its conversation.
    public static bool IsValid(HomeCase testCase)
    {
        if (testCase is not { ActorKind: "author", Document: { } document, Authoring: { } authoring, Spreadsheet: null, Audio: null }) return false;
        var kind = Kind(document.ContentType);
        var proposes = authoring.Capability is not null;
        return Regex.IsMatch(testCase.Id, "^home-document-[a-z0-9-]{1,80}-(es|en)$", RegexOptions.CultureInvariant)
            && testCase.Id.EndsWith("-" + testCase.Culture, StringComparison.Ordinal) && testCase.Culture is "es" or "en"
            && kind is not null && Regex.IsMatch(document.FileName, "^[a-z0-9-]+\\.(pdf|jpg|png|docx)$", RegexOptions.CultureInvariant)
            && Path.GetExtension(document.FileName) == (kind == "docx" ? ".docx" : QualificationDocuments.Extension(document.ContentType))
            && document.ByteCount is > 0 and <= StructuredAttachmentLimits.MaximumBytes
            && Regex.IsMatch(document.Sha256, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant)
            && (kind == "docx" ? document.Pages is null : kind == "pdf" ? document.Pages is >= 1 and <= StructuredAttachmentLimits.MaximumPages : document.Pages == 1)
            && (!document.Inherited || testCase.ConversationKey is not null)
            && (proposes ? authoring is { Capability: AuthoringOracle.ProposeForm, Version: AuthoringOracle.DocumentFormVersion }
                && document.Expected is { } expected && ValidExpectation(expected) : document.Expected is null);
    }

    private static bool ValidExpectation(HomeDocumentExpectation expected) =>
        expected.Facts is not null && expected.Facts.All(fact => FactType(fact) is not null) && expected.Facts.Distinct(StringComparer.Ordinal).Count() == expected.Facts.Length
        && expected.MinimumQuestions >= 1
        && (expected.Printed is null || expected.Printed.Length > 0 && expected.Printed.All(question => (question.Label is not null || question.Types is not null)
            && (question.Types is null || question.Types.Length > 0 && question.Types.All(type => QuestionTypes.Contains(type, StringComparer.Ordinal)))
            && (question.Options is null || question.Options.Length > 0 && question.Types is not null
                && question.Types.Any(type => type is "choice" or "multiChoice"))
            && (question.Types?.Contains("catalog", StringComparer.Ordinal) != true || question.Catalog is not null)
            && (question.Catalog is null || question.Types?.Contains("catalog", StringComparer.Ordinal) == true)));

    // The cases and the manifest agree: each document is the file the manifest has (what the provider reads as a photo or a PDF, a Word what
    // the server reads), each expected fact one it prints, the instruction one it hides; every case has its twin in the other culture with the
    // same document; an inherited document follows its start in the same conversation; and every reading has its document-reading case.
    public static bool Inventory(HomeCase[] cases, DocumentManifest manifest, DocumentSet readings)
    {
        if (cases.Length == 0 || !cases.All(IsValid) || cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != cases.Length) return false;
        foreach (var testCase in cases)
        {
            var document = testCase.Document!;
            var printed = manifest.Documents.SingleOrDefault(item => item.FileName == document.FileName);
            if (printed is null || printed.MediaType != document.ContentType || printed.ByteCount != document.ByteCount || printed.Sha256 != document.Sha256
                || printed.Pages != document.Pages || printed.Use != (IsWord(document) ? "server" : "provider")) return false;
            if (document.Expected is { } expected && (!expected.Facts.All(printed.Facts.ContainsKey) || expected.Injection && printed.Instruction is null)) return false;
            var twin = cases.SingleOrDefault(item => item.Id == Twin(testCase));
            if (twin is null || twin.Document!.FileName != document.FileName || twin.Document.Inherited != document.Inherited
                || (twin.Authoring!.Capability is null) != (testCase.Authoring!.Capability is null)) return false;
            var position = Array.IndexOf(cases, testCase);
            var start = cases.Take(position).LastOrDefault(item => item.ConversationKey == testCase.ConversationKey && testCase.ConversationKey is not null);
            if (document.Inherited ? start is null || start.Document!.FileName != document.FileName || start.Culture != testCase.Culture
                : testCase.ConversationKey is not null && start is not null) return false;
            if (!document.Inherited && !IsWord(document)
                && readings.Cases.Count(reading => reading.FileName == document.FileName && reading.Culture == testCase.Culture) != 1) return false;
        }
        return true;
    }

    private static string Twin(HomeCase testCase) => testCase.Id[..^2] + (testCase.Culture == "es" ? "en" : "es");

    public static string CasePath => Path.Combine(AppContext.BaseDirectory, "fixtures", CaseFile);
    public static string ReadingsPath => Path.Combine(AppContext.BaseDirectory, "fixtures", QualificationDocuments.CaseFile);

    public static HomeDocumentSet Load(byte[] bytes) =>
        JsonSerializer.Deserialize<HomeDocumentSet>(bytes, JsonSerializerOptions.Web) ?? throw new InvalidOperationException("Invalid public Home document cases.");

    // Before a run touches its ledger, database or provider, and before a listing: the case file names this exact manifest and these exact
    // document-reading cases, the manifest is the one those cases name, the cases agree with both, and every document has the size and
    // SHA-256 the cases pin.
    public static async Task<VerifiedHomeDocumentSet> LoadVerifiedAsync(CancellationToken cancellationToken)
    {
        var file = Load(await File.ReadAllBytesAsync(CasePath, cancellationToken));
        var manifestBytes = await File.ReadAllBytesAsync(QualificationDocuments.ManifestPath, cancellationToken);
        var readingBytes = await File.ReadAllBytesAsync(ReadingsPath, cancellationToken);
        var readings = QualificationDocuments.LoadSet();
        var manifest = QualificationDocuments.Manifest();
        if (!Verified(file, manifestBytes, readingBytes, readings, manifest)) throw new InvalidOperationException("Home document fixture inventory changed.");
        var cases = file.Cases;
        var set = new VerifiedHomeDocumentSet(cases, manifest, readings);
        foreach (var testCase in cases.Where(item => !item.Document!.Inherited).DistinctBy(item => item.Document!.FileName))
            _ = await ReadAsync(testCase.Document!, set.Document(testCase.Document!), cancellationToken);
        return set;
    }

    public static bool Verified(HomeDocumentSet file, byte[] manifestBytes, byte[] readingBytes, DocumentSet readings, DocumentManifest manifest) =>
        file is { ManifestSha256: not null, ReadingsSha256: not null, Cases: not null } && file.ManifestSha256 == EvidenceWriter.HashPublicBytes(manifestBytes)
        && file.ReadingsSha256 == ReadingsHash(readingBytes) && QualificationDocuments.Verified(readings, manifest, manifestBytes)
        && Inventory(file.Cases, manifest, readings);

    // The manifest keeps its exact bytes (`* -text`); the document-reading cases are text a checkout may write with CRLF, so they are pinned
    // by their LF form.
    public static string ReadingsHash(byte[] bytes) =>
        EvidenceWriter.HashPublicBytes(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal)));

    public static async Task<byte[]> ReadAsync(HomeDocumentFixture fixture, DocumentFixture document, CancellationToken cancellationToken)
    {
        if (fixture.FileName != document.FileName || fixture.ByteCount != document.ByteCount || fixture.Sha256 != document.Sha256)
            throw new InvalidOperationException("Invalid public Home document fixture.");
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.FromResult(QualificationDocuments.Read(document));
    }
}
