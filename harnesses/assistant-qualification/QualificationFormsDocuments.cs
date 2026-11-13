using System.Globalization;
using System.Resources;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace AssistantQualification;

// forms-documents (task 17b): «Armar formulario con la IA» (DOC6) from the received-documents inbox. A case asks for a form from one
// unclassified document ("document") or from the unclassified documents of a topic ("topic", in the order the inbox gives them), each with
// the reading the inbox keeps: a photo or a PDF read once by WhatsApp (ReadDocument in its sender's automatic conversation) and inherited by
// every later case of its culture that attaches it again, never read twice; a Word read by its owner on the server. The expectation is Home's
// (the reading on, off or either, the facts of the first document in their questions, the printed questions, the hidden instruction) plus the
// library standard: the one named (Standard), none, or none or any the turn's library read showed (AnyStandard). A blank form takes the
// reading either way (null): the request always asks for it and «Crear borrador» always keeps it, though propose-form@2 says never for one.
internal sealed record FormsDocumentExpectation(bool? DocumentReading, string[] Facts, string? Standard = null, bool AnyStandard = false,
    HomePrintedQuestion[]? Printed = null, int MinimumQuestions = 1, bool Injection = false)
{
    public HomeDocumentExpectation Document => new(DocumentReading, Facts, false, Printed, MinimumQuestions, Injection);
}
internal sealed record FormsDocumentCase(string Id, string Culture, string Source, HomeDocumentFixture[] Documents, string[] RequiredTools,
    FormsDocumentExpectation Expected);
// The case file names the exact manifest and document-reading cases it was written for, so the evidence (which hashes it) covers them.
internal sealed record FormsDocumentSet(string ManifestSha256, string ReadingsSha256, FormsDocumentCase[] Cases);
internal sealed record VerifiedFormsDocumentSet(FormsDocumentCase[] Cases, DocumentManifest Manifest, DocumentSet Readings)
{
    public DocumentFixture Document(HomeDocumentFixture fixture) => Manifest.Documents.Single(document => document.FileName == fixture.FileName);
    // A reading of the inbox expects what document-reading expects of the same file in the same culture: same candidate, same instructions.
    public DocumentCase Reading(FormsDocumentCase testCase, HomeDocumentFixture fixture) =>
        Readings.Cases.Single(reading => reading.FileName == fixture.FileName && reading.Culture == testCase.Culture);
}
// A reading the inbox keeps: the copy its reading opened (none for a Word, read on the server) and the document with that reading.
internal sealed record FormsSavedReading(string? Key, StructuredDocument Document);

internal static class QualificationFormsDocuments
{
    public const string CaseFile = "forms-document-cases.json";
    public const string ChannelId = "forms-documents";
    public const string Operation = "forms-documents";
    public const string TimeZoneId = "America/Argentina/Buenos_Aires";
    public const string Standards = "forms.library.standards";
    public const string TransferReceipt = "forms.transfer-receipt";
    // FormReceivedDocumentExtractionPolicy.ContentDays and FormDocumentAuthoringPolicy.MaxReadings: the content lives a week, five readings go.
    public const int ContentDays = 7;
    public const int MaxReadings = 1 + AssistantAttachmentInput.MaximumMoreDocuments;
    public static readonly string[] Sources = ["document", "topic"];
    // A copy of FormDocumentAuthoringPolicy (internal to Application, never reached by reflection): «Ver documentos recibidos», «Revisar
    // documentos» and «Diseñar formularios»; the authoring reads, the library read and propose-form@2; one proposal closes it. An offline
    // control holds the copy to the app's own test of the policy (FormDocumentAuthoringPolicyTests).
    public static readonly string[] Required = [BusinessFormsPermissions.DocumentsRead, BusinessFormsPermissions.DocumentsReview,
        BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage];
    public static readonly AssistantCapabilityReference ProposeForm = new(AuthoringOracle.ProposeForm, AuthoringOracle.DocumentFormVersion);
    public static readonly AssistantCapabilityReference[] Capabilities = [new("forms.authoring.forms", 1), new("forms.authoring.catalogs", 1),
        new(Standards, 1), ProposeForm];
    public static readonly AssistantTerminalReference[] Terminals = [new("assistant_proposal", 1)];
    // The reviewer's role: what the context requires plus reading catalogs, which forms.authoring.catalogs@1 asks for, so all four are offered.
    public static readonly string[] ReviewerPermissions = [..Required, BusinessFormsPermissions.CatalogsRead];
    private static readonly string[] Tools = [..Capabilities.Select(item => item.Id)];

    // The reviewer as Actor without a Subject, in the request's own conversation, with the reviewer's language and zone.
    public static AssistantChannelContext Context(Guid actorUserId, string culture, Guid requestId) =>
        new(ChannelId, actorUserId, null, culture, TimeZoneId, new(Required, []), Capabilities, Terminals) { ChannelConversationId = requestId };

    // What the request says (FormReceivedDocumentTexts.FormProposalRequest), in the reviewer's language.
    public static string RequestText(string culture)
    {
        if (culture is not ("es" or "en")) throw new ArgumentException("Unknown request culture.", nameof(culture));
        var resources = new ResourceManager("ArquitecturaBase.Application.Modules.BusinessForms.Resources.Documents", typeof(AssistantChannelContext).Assembly);
        return resources.GetString("FormProposalRequest", CultureInfo.GetCultureInfo(culture)) ?? throw new InvalidOperationException("Missing public request text.");
    }

    // The name the app gives a document WhatsApp sends without one (WhatsAppMessage.DocumentName): «image.jpg», «image.png», «document.pdf»
    // or «document.docx». Every document of the set goes with it, so no name tells the model what the document is.
    public static string ChannelName(string contentType) => WhatsAppMessage.DocumentName(null, contentType);

    public static string ReadingCaseId(FormsDocumentCase testCase, int index) => testCase.Id + "-" + (index + 1).ToString(CultureInfo.InvariantCulture);

    // The attachment FormDocumentAuthoringPolicy.Request builds: the first reading with its copy and half of the budget, the others after it.
    public static AssistantAttachmentInput Attachment(string key, IReadOnlyList<StructuredDocument> documents, DateTime expiresAtUtc) =>
        AssistantAttachmentInput.ForDocument(key, documents[0].FileName, expiresAtUtc, documents[0], documents.Count > 1 ? documents.Skip(1).ToArray() : null);

    // The proposal the page shows (FormDocumentAuthoringPolicy.Proposal): propose-form@2 with its form, the only proposal the channel offers.
    public static AssistantObservedProposal? Proposal(AssistantOperationResponse operation) =>
        operation.Proposals.FirstOrDefault(IsForm) ?? (operation.Proposal is { } proposal && IsForm(proposal) ? proposal : null);

    private static bool IsForm(AssistantObservedProposal proposal) => proposal.Capability == ProposeForm && proposal.Payload is FormAuthoringDocumentFormProposal;

    // A ledger of an interpretation names the reviewer, the channel and the culture and nothing else; its request is its conversation.
    public static bool IsLedger(QualificationLedgerEntry entry) => entry is { OperationKind: Operation, ChannelId: ChannelId, SubjectUserId: null,
            ChannelKind: null, SourceRecordId: null, PreviousRequestId: null, FocusDefinitionId: null, InteractionMode: AssistantInteractionMode.Unspecified }
        && entry.Culture is "es" or "en" && entry.CaseId.StartsWith("forms-document-", StringComparison.Ordinal)
        && !entry.CaseId.EndsWith("-document", StringComparison.Ordinal);

    public static AssistantChannelContext Resume(QualificationLedgerEntry entry) =>
        IsLedger(entry) ? Context(entry.ActorUserId, entry.Culture!, entry.RequestId) : throw new InvalidOperationException("Invalid forms-documents ledger scope.");

    // A case: one document or a topic of two to five; the first a photo or a PDF (its copy goes with it), a Word only after it and never
    // inherited (its owner reads it again on the server); the tools it must observe among the four offered, the proposal included; the
    // standard pinned only where the turn must read the library; a case whose reading may be off naming no standard and keeping no facts.
    public static bool IsValid(FormsDocumentCase testCase)
    {
        if (testCase is not { Documents: { Length: > 0 } documents, RequiredTools: { } tools, Expected: { Facts: not null } expected }
            || testCase.Culture is not ("es" or "en") || !testCase.Id.EndsWith("-" + testCase.Culture, StringComparison.Ordinal)
            || !Regex.IsMatch(testCase.Id, "^forms-document-[a-z0-9-]{1,60}-(es|en)$", RegexOptions.CultureInvariant)
            || (testCase.Source == "document" ? documents.Length != 1 : testCase.Source != "topic" || documents.Length is < 2 or > MaxReadings)
            || documents.Select(document => document.FileName).Distinct(StringComparer.Ordinal).Count() != documents.Length
            || QualificationHomeDocuments.IsWord(documents[0])) return false;
        if (documents.Any(document => document.Expected is not null || QualificationHomeDocuments.Kind(document.ContentType) is null
                || QualificationHomeDocuments.IsWord(document) && document.Inherited || document.ByteCount is <= 0 or > StructuredAttachmentLimits.MaximumBytes
                || !Regex.IsMatch(document.Sha256, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant))) return false;
        if (tools.Length == 0 || !tools.All(Tools.Contains) || tools.Distinct(StringComparer.Ordinal).Count() != tools.Length
            || !tools.Contains(AuthoringOracle.ProposeForm, StringComparer.Ordinal)) return false;
        if (expected.Standard is not (null or TransferReceipt) || expected.Standard is not null && (expected.AnyStandard || !tools.Contains(Standards, StringComparer.Ordinal))
            || expected.DocumentReading is not true && (expected.Standard is not null || expected.AnyStandard || expected.Facts.Length > 0)) return false;
        return QualificationHomeDocuments.ValidExpectation(expected.Document);
    }

    // The cases and the manifest agree: each document is the file the manifest has (a photo or a PDF the provider reads, a Word the server
    // reads); a photo or a PDF is read by the first case of its culture that attaches it and inherited by every later one, with its
    // document-reading case; the facts are the first document's; a hidden instruction is printed in one of them; every case has its twin in
    // the other culture with the same documents and the same expectation.
    public static bool Inventory(FormsDocumentCase[] cases, DocumentManifest manifest, DocumentSet readings)
    {
        if (cases.Length == 0 || !cases.All(IsValid) || cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != cases.Length) return false;
        for (var position = 0; position < cases.Length; position++)
        {
            var testCase = cases[position];
            var printed = new List<DocumentFixture>();
            foreach (var document in testCase.Documents)
            {
                var fixture = manifest.Documents.SingleOrDefault(item => item.FileName == document.FileName);
                var word = QualificationHomeDocuments.IsWord(document);
                if (fixture is null || fixture.MediaType != document.ContentType || fixture.ByteCount != document.ByteCount || fixture.Sha256 != document.Sha256
                    || fixture.Pages != document.Pages || fixture.Use != (word ? "server" : "provider")) return false;
                printed.Add(fixture);
                if (word) continue;
                var earlier = cases.Take(position).Any(item => item.Culture == testCase.Culture && item.Documents.Any(other => other.FileName == document.FileName));
                if (document.Inherited != earlier
                    || readings.Cases.Count(reading => reading.FileName == document.FileName && reading.Culture == testCase.Culture) != 1) return false;
            }
            var expected = testCase.Expected;
            if (!expected.Facts.All(printed[0].Facts.ContainsKey) || expected.Injection && printed.All(fixture => fixture.Instruction is null)) return false;
            var twin = cases.SingleOrDefault(item => item.Id == testCase.Id[..^2] + (testCase.Culture == "es" ? "en" : "es"));
            if (twin is null || twin.Source != testCase.Source || !twin.RequiredTools.SequenceEqual(testCase.RequiredTools, StringComparer.Ordinal)
                || !twin.Documents.Select(item => (item.FileName, item.Inherited)).SequenceEqual(testCase.Documents.Select(item => (item.FileName, item.Inherited)))
                || twin.Expected.DocumentReading != expected.DocumentReading || !twin.Expected.Facts.SequenceEqual(expected.Facts, StringComparer.Ordinal)
                || twin.Expected.Standard != expected.Standard || twin.Expected.AnyStandard != expected.AnyStandard
                || twin.Expected.MinimumQuestions != expected.MinimumQuestions || twin.Expected.Injection != expected.Injection
                || (twin.Expected.Printed is null) != (expected.Printed is null)) return false;
        }
        return true;
    }

    public static string CasePath => Path.Combine(AppContext.BaseDirectory, "fixtures", CaseFile);

    public static FormsDocumentSet Load(byte[] bytes) =>
        JsonSerializer.Deserialize<FormsDocumentSet>(bytes, JsonSerializerOptions.Web) ?? throw new InvalidOperationException("Invalid public forms-documents cases.");

    public static bool Verified(FormsDocumentSet file, byte[] manifestBytes, byte[] readingBytes, DocumentSet readings, DocumentManifest manifest) =>
        file is { ManifestSha256: not null, ReadingsSha256: not null, Cases: not null } && file.ManifestSha256 == EvidenceWriter.HashPublicBytes(manifestBytes)
        && file.ReadingsSha256 == QualificationHomeDocuments.ReadingsHash(readingBytes) && QualificationDocuments.Verified(readings, manifest, manifestBytes)
        && Inventory(file.Cases, manifest, readings);

    // Before a run touches its ledger, database or provider, and before a listing: the case file names this exact manifest and these exact
    // document-reading cases, the cases agree with both, and every document has the size and SHA-256 the cases pin.
    public static async Task<VerifiedFormsDocumentSet> LoadVerifiedAsync(CancellationToken cancellationToken)
    {
        var file = Load(await File.ReadAllBytesAsync(CasePath, cancellationToken));
        var manifestBytes = await File.ReadAllBytesAsync(QualificationDocuments.ManifestPath, cancellationToken);
        var readingBytes = await File.ReadAllBytesAsync(QualificationHomeDocuments.ReadingsPath, cancellationToken);
        var readings = QualificationDocuments.LoadSet();
        var manifest = QualificationDocuments.Manifest();
        if (!Verified(file, manifestBytes, readingBytes, readings, manifest)) throw new InvalidOperationException("Forms-documents fixture inventory changed.");
        var set = new VerifiedFormsDocumentSet(file.Cases, manifest, readings);
        foreach (var document in file.Cases.SelectMany(testCase => testCase.Documents).DistinctBy(document => document.FileName))
            _ = await QualificationHomeDocuments.ReadAsync(document, set.Document(document), cancellationToken);
        return set;
    }
}
