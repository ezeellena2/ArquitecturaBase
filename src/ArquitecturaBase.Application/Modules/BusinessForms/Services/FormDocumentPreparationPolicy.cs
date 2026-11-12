using System.Security.Cryptography;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// The reading of documents (Parte L): a document attached to the assistant is prepared within StructuredAttachmentLimits, and every
// rejection says what file it was. A PDF keeps its pages (none is unreadable, more than ten too many), a photo its kind within the
// limits of the photos of the forms, and a Word its text, read here: cut at MaximumDocumentCharacters, never in the middle of a
// character, with Truncated when the reader saw more or stopped short for its own limits, and Empty without any.
internal static class FormDocumentPreparationPolicy
{
    private const string PdfMediaType = "application/pdf";
    private const string JpegMediaType = "image/jpeg";
    private const string PngMediaType = "image/png";
    private const string DocxMediaType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    // A Word with macros or one older than 2007 is rejected by what it is, without reading it; null for the rest.
    public static StructuredAttachmentPreparation? Reject(FormDocumentFormat format) => format switch
    {
        FormDocumentFormat.WordMacroEnabled => Rejected(StructuredAttachmentRejection.MacroEnabled, StructuredAttachmentFileKind.Word),
        FormDocumentFormat.WordLegacy => Rejected(StructuredAttachmentRejection.Legacy, StructuredAttachmentFileKind.Word),
        _ => null,
    };

    public static StructuredAttachmentPreparation Prepare(string fileName, byte[] content, FormDocumentFormat format, FormDocumentContent read)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(read);
        var kind = format switch
        {
            FormDocumentFormat.Pdf => StructuredAttachmentFileKind.Pdf,
            FormDocumentFormat.Jpeg or FormDocumentFormat.Png => StructuredAttachmentFileKind.Image,
            FormDocumentFormat.Docx => StructuredAttachmentFileKind.Word,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Only a document that is read is prepared."),
        };
        if (read.Status == FormDocumentReadStatus.Protected) return Rejected(StructuredAttachmentRejection.Protected, kind);
        if (read.Status != FormDocumentReadStatus.Read) return Rejected(StructuredAttachmentRejection.Unreadable, kind);
        return format switch
        {
            FormDocumentFormat.Pdf => Pdf(fileName, content, read.Pages),
            FormDocumentFormat.Docx => Word(fileName, content, read.Text, read.Truncated),
            _ => Photo(fileName, content, format, read.Width, read.Height),
        };
    }

    private static StructuredAttachmentPreparation Pdf(string fileName, byte[] content, int? pages) => pages switch
    {
        null or < 1 => Rejected(StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Pdf),
        > StructuredAttachmentLimits.MaximumPages => Rejected(StructuredAttachmentRejection.TooManyPages, StructuredAttachmentFileKind.Pdf),
        _ => Prepared(fileName, content, StructuredDocumentKind.Pdf, PdfMediaType, pages, null),
    };

    private static StructuredAttachmentPreparation Photo(string fileName, byte[] content, FormDocumentFormat format, int? width, int? height)
    {
        if (width is not ({ } side and > 0) || height is not ({ } other and > 0))
            return Rejected(StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Image);
        if (side > StructuredAttachmentLimits.MaximumImageSide || other > StructuredAttachmentLimits.MaximumImageSide
            || (long)side * other > StructuredAttachmentLimits.MaximumImagePixels)
            return Rejected(StructuredAttachmentRejection.TooLarge, StructuredAttachmentFileKind.Image);
        return format == FormDocumentFormat.Jpeg
            ? Prepared(fileName, content, StructuredDocumentKind.Jpeg, JpegMediaType, null, null)
            : Prepared(fileName, content, StructuredDocumentKind.Png, PngMediaType, null, null);
    }

    private static StructuredAttachmentPreparation Word(string fileName, byte[] content, string? text, bool stoppedShort)
    {
        if (string.IsNullOrWhiteSpace(text)) return Rejected(StructuredAttachmentRejection.Empty, StructuredAttachmentFileKind.Word);
        var truncated = stoppedShort || text.Length > StructuredAttachmentLimits.MaximumDocumentCharacters;
        if (text.Length > StructuredAttachmentLimits.MaximumDocumentCharacters)
        {
            var kept = StructuredAttachmentLimits.MaximumDocumentCharacters;
            if (char.IsHighSurrogate(text[kept - 1])) kept--;
            text = text[..kept];
        }
        if (truncated) text = text.TrimEnd();
        return Prepared(fileName, content, StructuredDocumentKind.Docx, DocxMediaType, null,
            new StructuredDocumentReading(StructuredDocumentReadability.Readable, null, null, text, truncated));
    }

    private static StructuredAttachmentPreparation Prepared(string fileName, byte[] content, StructuredDocumentKind kind, string mediaType, int? pages,
        StructuredDocumentReading? reading) => StructuredAttachmentPreparation.Prepared(new StructuredDocument(fileName, kind, mediaType, content.LongLength,
        Convert.ToHexStringLower(SHA256.HashData(content)), pages, reading));

    private static StructuredAttachmentPreparation Rejected(StructuredAttachmentRejection rejection, StructuredAttachmentFileKind kind) =>
        StructuredAttachmentPreparation.Rejected(rejection, kind);
}
