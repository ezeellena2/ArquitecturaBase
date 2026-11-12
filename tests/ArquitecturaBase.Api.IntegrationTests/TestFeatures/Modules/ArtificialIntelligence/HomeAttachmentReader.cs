using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

// The owner's neutral port, fictional, so the IA tests never depend on another module: it allows everyone except Denied (nothing) and
// DocumentsDenied (only spreadsheets), refuses what is larger than an import reads and reads the bytes. «%PDF» is a PDF (with «pages=N»
// pages, one by default), a JPEG or PNG signature a photo, «DOCX» a Word whose text is the rest, «PK» a workbook and the rest CSV. A body
// «reject:<reason>[:<kind>]» is refused with that reason and kind. It never reads a real file: BusinessForms proves its own reader.
internal sealed class HomeAttachmentReader : IStructuredAttachmentReader
{
    private const string Rejected = "reject:";
    private const string WordMarker = "DOCX";
    public HashSet<Guid> Denied { get; } = [];
    public HashSet<Guid> DocumentsDenied { get; } = [];
    public int Prepared { get; private set; }

    public Task<StructuredAttachmentAccess> CanAttachAsync(Guid actorUserId, CancellationToken cancellationToken) =>
        Task.FromResult(new StructuredAttachmentAccess(!Denied.Contains(actorUserId), !Denied.Contains(actorUserId) && !DocumentsDenied.Contains(actorUserId)));

    public Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken cancellationToken)
    {
        if (content.LongLength > AssistantAttachmentInput.MaximumBytes)
            return Task.FromResult(StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.TooLarge));
        var text = Encoding.UTF8.GetString(content);
        if (text.StartsWith(Rejected, StringComparison.Ordinal))
        {
            var reason = text[Rejected.Length..].Split(':');
            return Task.FromResult(StructuredAttachmentPreparation.Rejected(Enum.Parse<StructuredAttachmentRejection>(reason[0]),
                reason.Length > 1 ? Enum.Parse<StructuredAttachmentFileKind>(reason[1]) : StructuredAttachmentFileKind.Unknown));
        }
        Prepared++;
        if (text.StartsWith("%PDF", StringComparison.Ordinal))
            return Task.FromResult(Document(content, fileName, StructuredDocumentKind.Pdf, "application/pdf", Pages(text), null));
        if (content is [0xFF, 0xD8, 0xFF, ..]) return Task.FromResult(Document(content, fileName, StructuredDocumentKind.Jpeg, "image/jpeg", null, null));
        if (content is [0x89, 0x50, 0x4E, 0x47, ..]) return Task.FromResult(Document(content, fileName, StructuredDocumentKind.Png, "image/png", null, null));
        if (text.StartsWith(WordMarker, StringComparison.Ordinal))
            return Task.FromResult(Document(content, fileName, StructuredDocumentKind.Docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                null, new StructuredDocumentReading(StructuredDocumentReadability.Readable, null, null, text[WordMarker.Length..].Trim(), false)));
        var workbook = content is [(byte)'P', (byte)'K', ..];
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var rows = lines.Take(6).Select((line, index) => new StructuredSpreadsheetRow(index + 1,
            line.Split(',').Select((cell, column) => (Letter: ((char)('A' + column)).ToString(), Cell: cell)).ToDictionary(cell => cell.Letter, cell => cell.Cell)))
            .ToArray();
        return Task.FromResult(StructuredAttachmentPreparation.Prepared(new StructuredSpreadsheet(fileName,
            workbook ? StructuredSpreadsheetKind.Xlsx : StructuredSpreadsheetKind.Csv,
            workbook ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/csv",
            content.LongLength, Sha256(content), workbook ? "Hoja1" : "", lines.Length,
            lines.Max(line => line.Split(',').Length), rows)));
    }

    private static int Pages(string text)
    {
        var at = text.IndexOf("pages=", StringComparison.Ordinal);
        return at < 0 ? 1 : int.Parse(new string(text[(at + "pages=".Length)..].TakeWhile(char.IsAsciiDigit).ToArray()), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static StructuredAttachmentPreparation Document(byte[] content, string fileName, StructuredDocumentKind kind, string mediaType, int? pages,
        StructuredDocumentReading? reading) => StructuredAttachmentPreparation.Prepared(new StructuredDocument(fileName, kind, mediaType, content.LongLength,
        Sha256(content), pages, reading));

    private static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
