using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Models.Assistant;

/// <summary>The file a turn carries: none, a spreadsheet or a read document. Sealed as a number in the turn's profile: never renumber.</summary>
public enum AssistantAttachmentKind { None = 0, Spreadsheet = 1, Document = 2 }

/// <summary>
/// The file attached to an interpretation: the descriptor of its private copy (server-owned, never accepted from HTTP or model
/// arguments) and exactly one of what its owner prepared from it: the first sheet of a spreadsheet or a read document. The engine never
/// opens the copy: the model and the capabilities read what was prepared. The member left out is omitted from the JSON, so a
/// spreadsheet sealed before documents reads and writes the same bytes.
/// </summary>
public sealed record AssistantAttachmentInput(string StorageKey, string Name, string ContentType, long Size, string Sha256,
    DateTime ExpiresAtUtc, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StructuredSpreadsheet? Spreadsheet)
{
    /// <summary>As much as the owner prepares, of any kind: one limit in every layer.</summary>
    public const long MaximumBytes = StructuredAttachmentLimits.MaximumBytes;

    /// <summary>
    /// What the read documents take, serialized with the web defaults, which escape each character outside ASCII: their own budget,
    /// apart from a spreadsheet's <see cref="StructuredSpreadsheet.MaximumBytes"/>.
    /// </summary>
    public const int DocumentsMaximumBytes = 16 * 1024;

    /// <summary>How many read documents may go after the first one (DOC6).</summary>
    public const int MaximumMoreDocuments = 4;

    /// <summary>A read document (a PDF, a photo or a Word, with its reading) instead of a spreadsheet; its copy is the descriptor's.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StructuredDocument? Document { get; init; }

    /// <summary>Up to <see cref="MaximumMoreDocuments"/> more read documents after the first one, data only, without a copy of their own.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<StructuredDocument>? MoreDocuments { get; init; }

    /// <summary>What the attachment carries: a document, a spreadsheet or, in an invalid one, nothing.</summary>
    [JsonIgnore]
    public AssistantAttachmentKind Kind => Document is not null ? AssistantAttachmentKind.Document
        : Spreadsheet is not null ? AssistantAttachmentKind.Spreadsheet : AssistantAttachmentKind.None;

    /// <summary>
    /// A read document attached as its owner built it: the descriptor of its copy takes the document's media type, size and SHA-256, and
    /// each document, the first and the ones after it, is fitted to its share of <see cref="DocumentsMaximumBytes"/> when it is built. It
    /// never builds what the engine rejects: one after the first that its share cannot admit (no text left, an unsafe name) is left out and
    /// the others share its place; a first one that cannot be admitted, or more than <see cref="MaximumMoreDocuments"/> after it, fail here.
    /// </summary>
    public static AssistantAttachmentInput ForDocument(string storageKey, string name, DateTime expiresAtUtc, StructuredDocument document,
        IReadOnlyList<StructuredDocument>? moreDocuments = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (moreDocuments is { Count: > MaximumMoreDocuments })
            throw new ArgumentOutOfRangeException(nameof(moreDocuments), moreDocuments.Count, "At most four read documents go after the first one.");
        List<StructuredDocument> kept = [.. moreDocuments ?? []];
        while (true)
        {
            var count = 1 + kept.Count;
            var fitted = kept.Select((other, index) => Fit(other, DocumentBytes(index + 1, count))).ToList();
            var admits = fitted.Select((other, index) => Admits(other, DocumentBytes(index + 1, count))).ToList();
            if (admits.Contains(false))
            {
                kept = [.. kept.Where((_, index) => admits[index])];
                continue;
            }
            var first = Fit(document, DocumentBytes(0, count));
            if (!Admits(first, DocumentBytes(0, count)))
                throw new ArgumentException("The read document cannot be attached: it needs a reading, a known kind, a safe name and room for its text.", nameof(document));
            return new(storageKey, name, document.MediaType, document.Size, document.Sha256, expiresAtUtc, null)
            {
                Document = first,
                MoreDocuments = fitted.Count == 0 ? null : fitted,
            };
        }
    }

    /// <summary>
    /// The share of <see cref="DocumentsMaximumBytes"/> of the document at <paramref name="index"/> among <paramref name="count"/>: all of
    /// it alone; with more, the first takes half and the others share the other half.
    /// </summary>
    public static int DocumentBytes(int index, int count) =>
        count <= 1 ? DocumentsMaximumBytes : index == 0 ? DocumentsMaximumBytes / 2 : DocumentsMaximumBytes / 2 / (count - 1);

    /// <summary>A file name as the person sees it: 1 to 255 characters, without folders or control characters.</summary>
    public static bool IsSafeName(string? name) =>
        name is { Length: >= 1 and <= 255 } && !name.Any(char.IsControl) && name.IndexOfAny(['/', '\\']) < 0 && name is not ("." or "..");

    /// <summary>
    /// Whether a read document goes in a share of <paramref name="maximumBytes"/>: with its reading, a known kind and a safe name, some text
    /// unless nothing could be read, and within those bytes as <see cref="SerializedBytes"/> measures them. The one rule of the builder and
    /// of the engine's validation, which also checks the reading itself.
    /// </summary>
    public static bool Admits(StructuredDocument? document, int maximumBytes) =>
        document is { Reading: { } reading } && Enum.IsDefined(document.Kind) && IsSafeName(document.FileName)
        && (reading.Readability == StructuredDocumentReadability.Unreadable || !string.IsNullOrWhiteSpace(reading.Text))
        && SerializedBytes(document) <= maximumBytes;

    /// <summary>The bytes a document takes serialized with the web defaults, the measure of its budget.</summary>
    public static int SerializedBytes(StructuredDocument document) => JsonSerializer.SerializeToUtf8Bytes(document, JsonSerializerOptions.Web).Length;

    /// <summary>
    /// The document within <paramref name="maximumBytes"/>: as it came when it fits; if not, its reading loses its leading blanks and keeps
    /// the longest beginning of the rest that fits, never half of a surrogate pair, and says it was cut. Always the same cut for the same
    /// document; it may keep no text, which <see cref="Admits"/> then rejects.
    /// </summary>
    public static StructuredDocument Fit(StructuredDocument document, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Reading is not { } reading || SerializedBytes(document) <= maximumBytes) return document;
        // From the first visible character, so a cut never keeps only blanks; the serialized size only grows with the text, so the longest
        // beginning that fits, the whole rest included, is found by halves.
        var text = reading.Text.TrimStart();
        var (fits, over) = (0, text.Length + 1);
        while (over - fits > 1)
        {
            var middle = fits + (over - fits) / 2;
            if (SerializedBytes(Cut(document, reading, text, middle)) <= maximumBytes) fits = middle;
            else over = middle;
        }
        if (fits > 0 && char.IsHighSurrogate(text[fits - 1])) fits--;
        return Cut(document, reading, text, fits);
    }

    private static StructuredDocument Cut(StructuredDocument document, StructuredDocumentReading reading, string text, int length) =>
        document with { Reading = reading with { Text = text[..length], Truncated = true } };

    public override string ToString() => "AssistantAttachmentInput { Content = [REDACTED] }";
}

/// <summary>
/// The private copy of a turn's file, its own or inherited, as the engine sealed it: its key, the person's file name, its media type,
/// size and SHA-256, for the owner to open the copy and check it is the same file.
/// </summary>
public sealed record AssistantAttachmentDescriptor(string StorageKey, string Name, string ContentType, long Size, string Sha256)
{
    public override string ToString() => "AssistantAttachmentDescriptor { Content = [REDACTED] }";
}
