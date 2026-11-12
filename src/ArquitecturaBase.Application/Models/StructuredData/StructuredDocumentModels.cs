using System.Text.Json.Serialization;

namespace ArquitecturaBase.Application.Models.StructuredData;

public enum StructuredDocumentKind { Pdf, Jpeg, Png, Docx }

/// <summary>How much of a document could be read: all of it, part of it or nothing.</summary>
public enum StructuredDocumentReadability { Readable, Partial, Unreadable }

/// <summary>Why a photo or a page is not read whole: blurry, dark, cut, covered by something or another reason.</summary>
public enum StructuredDocumentProblem { Blurry, Dark, Cut, Covered, Other }

/// <summary>
/// What was read of a document: whether it could be read and, when not whole, why; its language when it is known; its text in
/// order, tables as Markdown rows; and whether the text was cut. The owner reads a Word itself; a PDF and a photo are read by the
/// engine. The optional members are left out of the JSON when null.
/// </summary>
public sealed record StructuredDocumentReading(StructuredDocumentReadability Readability,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StructuredDocumentProblem? Problem,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Language,
    string Text, bool Truncated)
{
    public override string ToString() => "StructuredDocumentReading { Content = [REDACTED] }";
}

/// <summary>
/// A document attached to the assistant as its owner prepared it: the file (name, kind, media type, size and SHA-256 of the original
/// bytes), the pages of a PDF and, only for a Word, its reading. A PDF and a photo get theirs from the engine. The optional members
/// are left out of the JSON when null.
/// </summary>
public sealed record StructuredDocument(string FileName, StructuredDocumentKind Kind, string MediaType, long Size, string Sha256,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Pages,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StructuredDocumentReading? Reading)
{
    public override string ToString() => "StructuredDocument { Content = [REDACTED] }";
}
