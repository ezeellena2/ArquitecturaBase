using System.IO.Compression;
using System.Text.Unicode;
using System.Xml;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Import;

internal sealed partial class ClosedXmlFormSpreadsheetReader
{
    private const int MaximumManifestBytes = 64 * 1024;
    private const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private static ReadOnlySpan<byte> CompoundSignature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    // What the bytes are, never the name. An OLE2 (CFB) container is an old .xls or a workbook with a password: telling them
    // apart needs another parser. A ZIP is a workbook by the type its OOXML manifest gives to its main part; an Excel part
    // enabled for macros (.xlsm, .xltm, .xlam, .xlsb) is rejected even next to a workbook one. UTF-8 text is a CSV.
    public FormSpreadsheetFormat DetectFormat(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ReadOnlySpan<byte> bytes = content;
        if (bytes.StartsWith(CompoundSignature)) return FormSpreadsheetFormat.LegacyOrProtected;
        if (bytes.StartsWith("PK\u0003\u0004"u8)) return WorkbookFormat(content);
        return Utf8.IsValid(bytes) ? FormSpreadsheetFormat.Csv : FormSpreadsheetFormat.Unknown;
    }

    private static FormSpreadsheetFormat WorkbookFormat(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var manifest = archive.GetEntry("[Content_Types].xml");
            if (manifest is null || manifest.Length > MaximumManifestBytes) return FormSpreadsheetFormat.Unknown;
            using var source = manifest.Open();
            using var xml = XmlReader.Create(source, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumManifestBytes,
            });
            var format = FormSpreadsheetFormat.Unknown;
            while (xml.Read())
            {
                if (xml.NodeType != XmlNodeType.Element || xml.LocalName is not ("Override" or "Default")) continue;
                var type = xml.GetAttribute("ContentType");
                if (type is null) continue;
                if (type.StartsWith("application/vnd.ms-excel.", StringComparison.Ordinal) && type.Contains(".macroEnabled.main", StringComparison.Ordinal))
                    return FormSpreadsheetFormat.MacroEnabled;
                if (type == WorkbookContentType) format = FormSpreadsheetFormat.Xlsx;
            }
            return format;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or XmlException or NotSupportedException)
        {
            return FormSpreadsheetFormat.Unknown;
        }
    }
}
