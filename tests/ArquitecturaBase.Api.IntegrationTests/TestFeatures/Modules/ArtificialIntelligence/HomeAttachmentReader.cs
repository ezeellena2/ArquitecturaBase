using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

// The owner's neutral port, fictional, so the IA tests never depend on another module: it allows everyone except Denied and reads the
// bytes as CSV text, with its first rows by column letter. A body «reject:<reason>» is refused with that reason. It never reads a
// real workbook: BusinessForms proves its own reader.
internal sealed class HomeAttachmentReader : IStructuredAttachmentReader
{
    private const string Rejected = "reject:";
    public HashSet<Guid> Denied { get; } = [];
    public int Prepared { get; private set; }

    public Task<bool> CanAttachAsync(Guid actorUserId, CancellationToken cancellationToken) => Task.FromResult(!Denied.Contains(actorUserId));

    public Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken cancellationToken)
    {
        var text = Encoding.UTF8.GetString(content);
        if (text.StartsWith(Rejected, StringComparison.Ordinal))
            return Task.FromResult(StructuredAttachmentPreparation.Rejected(Enum.Parse<StructuredAttachmentRejection>(text[Rejected.Length..])));
        Prepared++;
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var rows = lines.Take(6).Select((line, index) => new StructuredSpreadsheetRow(index + 1,
            line.Split(',').Select((cell, column) => (Letter: ((char)('A' + column)).ToString(), Cell: cell)).ToDictionary(cell => cell.Letter, cell => cell.Cell)))
            .ToArray();
        return Task.FromResult(StructuredAttachmentPreparation.Prepared(new StructuredSpreadsheet(fileName, StructuredSpreadsheetKind.Csv, "text/csv",
            content.LongLength, Convert.ToHexStringLower(SHA256.HashData(content)), "", lines.Length, lines.Max(line => line.Split(',').Length), rows)));
    }
}
