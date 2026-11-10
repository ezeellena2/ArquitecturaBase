using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

// The owner's neutral port, fictional, so the IA tests never depend on another module: it allows everyone except Denied, refuses what
// is larger than an import reads and reads the bytes as text, with its first rows by column letter. Bytes that open like a ZIP («PK»)
// are a workbook; the rest, CSV. A body «reject:<reason>» is refused with that reason. It never reads a real workbook: BusinessForms
// proves its own reader.
internal sealed class HomeAttachmentReader : IStructuredAttachmentReader
{
    private const string Rejected = "reject:";
    public HashSet<Guid> Denied { get; } = [];
    public int Prepared { get; private set; }

    public Task<bool> CanAttachAsync(Guid actorUserId, CancellationToken cancellationToken) => Task.FromResult(!Denied.Contains(actorUserId));

    public Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken cancellationToken)
    {
        if (content.LongLength > AssistantAttachmentInput.MaximumBytes)
            return Task.FromResult(StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.TooLarge));
        var text = Encoding.UTF8.GetString(content);
        if (text.StartsWith(Rejected, StringComparison.Ordinal))
            return Task.FromResult(StructuredAttachmentPreparation.Rejected(Enum.Parse<StructuredAttachmentRejection>(text[Rejected.Length..])));
        Prepared++;
        var workbook = content is [(byte)'P', (byte)'K', ..];
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var rows = lines.Take(6).Select((line, index) => new StructuredSpreadsheetRow(index + 1,
            line.Split(',').Select((cell, column) => (Letter: ((char)('A' + column)).ToString(), Cell: cell)).ToDictionary(cell => cell.Letter, cell => cell.Cell)))
            .ToArray();
        return Task.FromResult(StructuredAttachmentPreparation.Prepared(new StructuredSpreadsheet(fileName,
            workbook ? StructuredSpreadsheetKind.Xlsx : StructuredSpreadsheetKind.Csv,
            workbook ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/csv",
            content.LongLength, Convert.ToHexStringLower(SHA256.HashData(content)), workbook ? "Hoja1" : "", lines.Length,
            lines.Max(line => line.Split(',').Length), rows)));
    }
}
