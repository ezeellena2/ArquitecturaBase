using System.IO.Compression;
using System.Text.Unicode;
using System.Xml;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Files;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Import;

internal sealed partial class ClosedXmlFormSpreadsheetReader
{
    private const int MaximumManifestBytes = 64 * 1024;
    private const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";

    // What the bytes are, never the name. An OLE2 container is an old Excel by its «Workbook» or «Book» stream, or a file with a
    // password by its «EncryptedPackage»; any other (a .doc, a .msg) is unknown. A ZIP is a workbook by the ContentType of any
    // Override or Default of its OOXML manifest, and an Excel type enabled for macros (.xlsm, .xltm, .xlam, .xlsb) wins over a
    // workbook one. UTF-8 text is a CSV.
    public FormSpreadsheetFormat DetectFormat(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (CompoundDocument.IsCompound(content)) return CompoundFormat(content);
        if (content.AsSpan().StartsWith("PK\u0003\u0004"u8)) return WorkbookFormat(content);
        return Utf8.IsValid(content) ? FormSpreadsheetFormat.Csv : FormSpreadsheetFormat.Unknown;
    }

    private static FormSpreadsheetFormat CompoundFormat(byte[] content)
    {
        try
        {
            if (CompoundDocument.HasStream(content, "EncryptedPackage")) return FormSpreadsheetFormat.Protected;
            return CompoundDocument.HasStream(content, "Workbook", "Book") ? FormSpreadsheetFormat.Legacy : FormSpreadsheetFormat.Unknown;
        }
        catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException or ArgumentException
            or InvalidOperationException)
        {
            return FormSpreadsheetFormat.Unknown;
        }
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
