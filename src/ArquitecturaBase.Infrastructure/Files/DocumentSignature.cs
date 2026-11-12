using System.IO.Compression;
using System.Xml;

namespace ArquitecturaBase.Infrastructure.Files;

// What makes some bytes the document their media type says, as the private files accept them: a PDF starts with its header and
// says «%%EOF» in its last KiB; an OOXML file has its main part and declares it in its manifest with an Override of that type, read
// as XML of at most 64 KiB without DTD. The uploads use it and so does the reader of the documents attached to the assistant, so a
// document prepared there is never refused when it is stored. A damaged ZIP throws its InvalidDataException or IOException.
internal static class DocumentSignature
{
    private const int MaximumManifestBytes = 64 * 1024;

    public static bool IsPdf(ReadOnlySpan<byte> content) =>
        content.StartsWith("%PDF-"u8) && content[Math.Max(0, content.Length - 1024)..].IndexOf("%%EOF"u8) >= 0;

    public static bool IsOpenXml(byte[] content, string mainPart, string contentType)
    {
        using var stream = new MemoryStream(content, false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var manifest = archive.GetEntry("[Content_Types].xml");
        if (manifest is null || manifest.Length > MaximumManifestBytes || archive.GetEntry(mainPart) is null) return false;
        using var xml = manifest.Open();
        using var reader = XmlReader.Create(xml, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumManifestBytes,
        });
        while (reader.Read())
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Override"
                && reader.GetAttribute("PartName") == "/" + mainPart && reader.GetAttribute("ContentType") == contentType) return true;
        return false;
    }
}
