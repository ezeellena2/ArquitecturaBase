namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

// What some bytes are as a document, never by their name: a PDF by its header, a JPEG or PNG photo by its signature (SkiaSharp
// confirms it when reading), a Word .docx by the main part of its OOXML manifest, and the Word files that are not read: one with
// macros (.docm) and one older than 2007 (.doc, an OLE2 «WordDocument» stream). Unknown is not a document: a spreadsheet, a CSV
// or anything else, which the spreadsheet reader then decides.
public enum FormDocumentFormat { Unknown, Pdf, Jpeg, Png, Docx, WordMacroEnabled, WordLegacy }

// Whether the bytes opened as their format said: Protected is a PDF that asks for a password to open.
public enum FormDocumentReadStatus { Read, Protected, Unreadable }

// What was read of a document without rendering, decoding its pixels or running anything: a PDF's pages, a photo's size as its
// header declares it and a Word's text in order, read up to one character past StructuredAttachmentLimits.MaximumDocumentCharacters,
// so a longer text says the document goes on. Truncated says the reader stopped before the end for another of its limits.
public sealed record FormDocumentContent(FormDocumentReadStatus Status, int? Pages = null, int? Width = null, int? Height = null,
    string? Text = null, bool Truncated = false)
{
    public override string ToString() => $"FormDocumentContent {{ Status = {Status}, Content = [REDACTED] }}";
}
