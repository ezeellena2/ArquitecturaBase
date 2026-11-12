using System.Text.Json;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;

namespace AssistantQualification;

// A fictitious document of fixtures/documents as generate.py wrote it (manifest.json): the file, how it is used (read by the provider,
// read on the server or rejected by the port), what it prints in reading order and the facts a reading has to keep, by their id.
internal sealed record DocumentRejection(string Reason, string FileKind);
internal sealed record DocumentFixture(string FileName, string MediaType, long ByteCount, string Sha256, int? Pages, string Use, string Kind,
    string Description, IReadOnlyDictionary<string, string> Facts, string? InvalidOnPurpose, string[] Words, string? Instruction,
    DocumentRejection? Rejection, string[] Text);
internal sealed record DocumentManifest(string Status, string Generator, string Command, IReadOnlyDictionary<string, string> Tools,
    string[] Rules, DocumentFixture[] Documents);

// What the reading of a case must show: the readability and the problem it may have (absent, any), the manifest facts whose digits it
// keeps, whether it keeps the document's own words untranslated, its language (absent, not checked), the instruction it transcribes and,
// for a photo that is not a document, nothing but the stamp.
internal sealed record DocumentExpectation(string[]? Readability, string?[]? Problem, string[] Facts, bool Words, string? Language,
    bool Instruction = false, bool OnlyStamp = false);
internal sealed record DocumentCase(string Id, string Culture, string FileName, string ContentType, long ByteCount, string Sha256, int Pages,
    DocumentExpectation Expected);
// The case file names the exact manifest it was written for, so the evidence (which hashes the case file) covers the manifest too.
internal sealed record DocumentSet(string ManifestSha256, DocumentCase[] Cases);
internal sealed record VerifiedDocumentSet(DocumentCase[] Cases, DocumentManifest Manifest);

// The document-reading set (Parte L, task 4): ReadDocument alone, in a channel of the harness, es and en. Each case is one reading of a
// committed fixture, prepared first by the real port as Home and WhatsApp will prepare it.
internal static class QualificationDocuments
{
    public const string CaseFile = "document-reading-cases.json";
    public const string ChannelId = "qualification-documents";
    public const string Stamp = "EJEMPLO · FICTICIO";
    public const int ProviderDocuments = 15;
    private const string KeyPrefix = "qualification/documents/";
    public static readonly string[] Readabilities = ["readable", "partial", "unreadable"];
    public static readonly string[] Problems = ["blurry", "dark", "cut", "covered", "other"];
    public static readonly string[] Uses = ["provider", "server", "rejected"];

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "fixtures", "documents");
    public static string ManifestPath => Path.Combine(Folder, "manifest.json");

    public static DocumentManifest Manifest() => ParseManifest(File.ReadAllBytes(ManifestPath));

    public static DocumentSet LoadSet() =>
        JsonSerializer.Deserialize<DocumentSet>(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", CaseFile)), JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Invalid public document cases.");

    // Before a run touches its ledger, database or provider, and before a listing: the cases name this exact manifest and agree with it,
    // and every file the provider reads has the size and SHA-256 they pin. A changed fixture stops here, never halfway through a paid run.
    public static async Task<VerifiedDocumentSet> LoadVerifiedAsync(CancellationToken cancellationToken)
    {
        var manifestBytes = await File.ReadAllBytesAsync(ManifestPath, cancellationToken);
        var set = LoadSet();
        var manifest = ParseManifest(manifestBytes);
        if (!Verified(set, manifest, manifestBytes)) throw new InvalidOperationException("Document fixture inventory changed.");
        foreach (var testCase in set.Cases.DistinctBy(testCase => testCase.FileName)) _ = await ReadAsync(testCase, cancellationToken);
        return new(set.Cases, manifest);
    }

    public static bool Verified(DocumentSet set, DocumentManifest manifest, byte[] manifestBytes) =>
        set is { ManifestSha256: not null, Cases: not null } && set.ManifestSha256 == EvidenceWriter.HashPublicBytes(manifestBytes)
        && Inventory(set.Cases, manifest);

    private static DocumentManifest ParseManifest(byte[] bytes) =>
        JsonSerializer.Deserialize<DocumentManifest>(bytes, JsonSerializerOptions.Web) ?? throw new InvalidOperationException("Invalid public document manifest.");

    // A case names a PDF, a JPEG or a PNG the provider reads, within the attachment limits; a photo is one page.
    public static bool IsValid(DocumentCase testCase)
    {
        var expected = testCase.Expected;
        return Regex.IsMatch(testCase.Id, "^document-[a-z0-9-]{1,80}-(es|en)$", RegexOptions.CultureInvariant)
            && testCase.Id.EndsWith("-" + testCase.Culture, StringComparison.Ordinal)
            && Regex.IsMatch(testCase.FileName, "^[a-z0-9-]+\\.(pdf|jpg|png)$", RegexOptions.CultureInvariant)
            && Kind(testCase.ContentType) is { } kind && Extension(testCase.ContentType) == Path.GetExtension(testCase.FileName)
            && testCase.ByteCount is > 0 and <= StructuredAttachmentLimits.MaximumBytes
            && Regex.IsMatch(testCase.Sha256, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant)
            && testCase.Pages is >= 1 and <= StructuredAttachmentLimits.MaximumPages && (kind == StructuredDocumentKind.Pdf || testCase.Pages == 1)
            && expected is not null && expected.Facts is not null
            && (expected.Readability is null || expected.Readability.Length > 0 && expected.Readability.All(value => Readabilities.Contains(value, StringComparer.Ordinal)))
            && (expected.Problem is null || expected.Problem.Length > 0 && expected.Problem.All(value => value is null || Problems.Contains(value, StringComparer.Ordinal)))
            && expected.Language is null or "es" or "en";
    }

    // The manifest and the cases agree: every document the provider reads has exactly one case per culture with its file as the manifest
    // has it, and every fact a case expects is one its document prints.
    public static bool Inventory(DocumentCase[] cases, DocumentManifest manifest)
    {
        var provider = manifest.Documents.Where(document => document.Use == "provider").ToArray();
        return provider.Length == ProviderDocuments && cases.Length == 2 * ProviderDocuments && cases.All(IsValid)
            && cases.Select(testCase => testCase.Id).Distinct(StringComparer.Ordinal).Count() == cases.Length
            && provider.All(document => cases.Count(testCase => testCase.FileName == document.FileName) == 2
                && cases.Where(testCase => testCase.FileName == document.FileName).Select(testCase => testCase.Culture).Order(StringComparer.Ordinal)
                    .SequenceEqual(["en", "es"]))
            && cases.All(testCase => provider.SingleOrDefault(document => document.FileName == testCase.FileName) is { } document
                && document.MediaType == testCase.ContentType && document.ByteCount == testCase.ByteCount && document.Sha256 == testCase.Sha256
                && document.Pages == testCase.Pages && testCase.Expected.Facts.All(document.Facts.ContainsKey)
                && (!testCase.Expected.Instruction || document.Instruction is not null) && (!testCase.Expected.Words || document.Words.Length > 0));
    }

    public static async Task<byte[]> ReadAsync(DocumentCase testCase, CancellationToken cancellationToken)
    {
        if (!IsValid(testCase)) throw new InvalidOperationException("Invalid public document fixture.");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(Folder, testCase.FileName), cancellationToken);
        if (bytes.LongLength != testCase.ByteCount || EvidenceWriter.HashPublicBytes(bytes) != testCase.Sha256)
            throw new InvalidOperationException("Public document fixture changed.");
        return bytes;
    }

    public static byte[] Read(DocumentFixture document)
    {
        if (!Regex.IsMatch(document.FileName, "^[a-z0-9-]+\\.(pdf|jpg|png|docx|docm|doc)$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Invalid public document fixture.");
        var bytes = File.ReadAllBytes(Path.Combine(Folder, document.FileName));
        if (bytes.LongLength != document.ByteCount || EvidenceWriter.HashPublicBytes(bytes) != document.Sha256)
            throw new InvalidOperationException("Public document fixture changed.");
        return bytes;
    }

    // What the owner prepared from the public bytes is the case's document: a PDF or a photo with its pages and without a reading, which
    // only the engine gives it.
    public static bool PreparedAsExpected(DocumentCase testCase, StructuredAttachmentPreparation prepared) =>
        prepared is { Rejection: null, Spreadsheet: null, Document: { Reading: null } document } && document.FileName == testCase.FileName
        && document.Kind == Kind(testCase.ContentType) && document.MediaType == testCase.ContentType && document.Size == testCase.ByteCount
        && document.Sha256 == testCase.Sha256 && (document.Pages ?? 1) == testCase.Pages
        && (document.Kind == StructuredDocumentKind.Pdf) == document.Pages.HasValue;

    // ReadDocument alone in a channel of its own: the person as Actor, no Subject, nothing required, offered or allowed to end the turn.
    public static AssistantChannelContext Context(Guid actorUserId, string culture) => new(ChannelId, actorUserId, null, culture,
        "America/Argentina/Buenos_Aires", new([], []), [], []);

    // A ledger names its actor, the channel, the culture and nothing else; a scope that says more was not this set's.
    public static AssistantChannelContext Resume(QualificationLedgerEntry entry)
    {
        if (entry is not { OperationKind: "document", ChannelId: ChannelId, SubjectUserId: null, ChannelKind: null, SourceRecordId: null,
                PreviousRequestId: null, InteractionMode: AssistantInteractionMode.Unspecified } || entry.Culture is not ("es" or "en")
            || !entry.CaseId.StartsWith("document-", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid document ledger scope.");
        return Context(entry.ActorUserId, entry.Culture);
    }

    // The copy the engine opens: a key of the harness derived from the request, never the person's file name.
    public static string Key(Guid requestId, string mediaType) =>
        KeyPrefix + requestId.ToString("N") + (Extension(mediaType) ?? throw new InvalidOperationException("Unsupported document type."));
    public static bool IsKey(string key) => Regex.IsMatch(key, "^qualification/documents/[a-f0-9]{32}\\.(pdf|jpg|png)$", RegexOptions.CultureInvariant);

    // The descriptor the server seals for the engine, from what the owner prepared: a photo is one page.
    public static AssistantDocumentInput Descriptor(string key, StructuredDocument document, DateTime expiresAtUtc) =>
        new(key, document.FileName, document.MediaType, document.Size, document.Sha256, document.Pages ?? 1, expiresAtUtc);

    public static StructuredDocumentKind? Kind(string mediaType) => mediaType switch
    {
        "application/pdf" => StructuredDocumentKind.Pdf,
        "image/jpeg" => StructuredDocumentKind.Jpeg,
        "image/png" => StructuredDocumentKind.Png,
        _ => null,
    };

    public static string? Extension(string mediaType) => mediaType switch
    {
        "application/pdf" => ".pdf",
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        _ => null,
    };

    // Every account key and CUIT a document prints, wherever it is: runs of 22 digits and NN-NNNNNNNN-N.
    public static IEnumerable<string> PrintedKeys(IEnumerable<string> lines) => lines.SelectMany(line =>
        Regex.Matches(line, "(?<!\\d)\\d{22}(?!\\d)", RegexOptions.CultureInvariant).Select(match => match.Value)
            .Concat(Regex.Matches(line, "(?<!\\d)\\d{2}-\\d{8}-\\d(?!\\d)", RegexOptions.CultureInvariant).Select(match => match.Value)));

    // A CBU or a CVU: 22 digits in two blocks, each with its check digit (weights 7, 1, 3, 9, 7, 1, 3 and 3, 9, 7, 1, 3, 9, 7, 1, 3, 9, 7,
    // 1, 3; each check is (10 - sum % 10) % 10). A CUIT or a CUIL: eleven digits with a modulo 11 check, where 10 is never valid.
    public static bool IsBankKey(string value) =>
        value.Length == 22 && value.All(char.IsAsciiDigit) && Check(value.AsSpan(0, 7), [7, 1, 3, 9, 7, 1, 3]) == value[7] - '0'
        && Check(value.AsSpan(8, 13), [3, 9, 7, 1, 3, 9, 7, 1, 3, 9, 7, 1, 3]) == value[21] - '0';

    public static bool IsCuit(string value)
    {
        if (!Regex.IsMatch(value, "^\\d{2}-\\d{8}-\\d$", RegexOptions.CultureInvariant)) return false;
        var digits = value.Replace("-", "", StringComparison.Ordinal);
        int[] weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
        var check = 11 - weights.Select((weight, index) => weight * (digits[index] - '0')).Sum() % 11;
        return check != 10 && digits[10] - '0' == (check == 11 ? 0 : check);
    }

    private static int Check(ReadOnlySpan<char> digits, int[] weights)
    {
        var sum = 0;
        for (var index = 0; index < digits.Length; index++) sum += (digits[index] - '0') * weights[index];
        return (10 - sum % 10) % 10;
    }
}
