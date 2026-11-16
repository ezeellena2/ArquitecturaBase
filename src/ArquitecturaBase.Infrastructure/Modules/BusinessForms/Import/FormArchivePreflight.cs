using System.IO.Compression;
using System.Xml;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Import;

// The ZIP preflight of a workbook and of a Word, before a library opens them: every entry once, within MaximumArchiveBytes by the
// sizes declared and by the bytes ZipArchive delivers (in .NET 10 a forged shorter length truncates the delivery to it, so the
// central directory is not read again). Nothing is extracted to disk.
internal static class FormArchivePreflight
{
    // Parts a library loads whole into memory: each has its own cap under the archive one, judged by the declared size (a forged shorter one truncates the delivery).
    // Names are compared without regard to case, and the parts the workbook's own relationships name count too (a workbook may keep them elsewhere).
    private static readonly Dictionary<string, long> DefaultPartLimits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["xl/styles.xml"] = FormCatalogImportLimits.MaximumStylesBytes,
        ["xl/sharedStrings.xml"] = FormCatalogImportLimits.MaximumSharedStringsBytes,
    };

    private const string WorkbookRelationships = "xl/_rels/workbook.xml.rels";
    private const long MaximumRelationshipsBytes = 1024 * 1024;

    internal static Dictionary<string, long> PartLimits(ZipArchive archive)
    {
        var limits = new Dictionary<string, long>(DefaultPartLimits, StringComparer.OrdinalIgnoreCase);
        var rels = archive.Entries.FirstOrDefault(entry => string.Equals(entry.FullName, WorkbookRelationships, StringComparison.OrdinalIgnoreCase));
        if (rels is null || rels.Length > MaximumRelationshipsBytes) return limits;
        try
        {
            using var stream = rels.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship") continue;
                var type = reader.GetAttribute("Type");
                var target = reader.GetAttribute("Target");
                if (target is null || type is null) continue;
                var limit = type.EndsWith("/styles", StringComparison.Ordinal) ? FormCatalogImportLimits.MaximumStylesBytes
                    : type.EndsWith("/sharedStrings", StringComparison.Ordinal) ? FormCatalogImportLimits.MaximumSharedStringsBytes : 0;
                if (limit == 0 || reader.GetAttribute("TargetMode") == "External") continue;
                var path = new Uri(new Uri("http://x/xl/"), target.Replace("\\", "/")).AbsolutePath.TrimStart('/');
                limits[Uri.UnescapeDataString(path)] = limit;
            }
        }
        catch (Exception exception) when (exception is XmlException or UriFormatException) { }
        return limits;
    }

    public static Result Check(ZipArchive archive, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var partLimits = PartLimits(archive);
        long declared = 0; long expanded = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        var buffer = new byte[81920];
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (!names.Add(entry.FullName)) return FormCatalogImportErrors.FileUnreadable;
            if (entry.Length > FormCatalogImportLimits.MaximumArchiveBytes - declared) return FormCatalogImportErrors.FileTooLarge;
            if (partLimits.TryGetValue(entry.FullName, out var partLimit) && entry.Length > partLimit) return FormCatalogImportErrors.FileTooLarge;
            declared += entry.Length;
            using var source = entry.Open();
            int count;
            while ((count = source.Read(buffer, 0, (int)Math.Min(buffer.Length,
                FormCatalogImportLimits.MaximumArchiveBytes + 1L - expanded))) > 0)
            {
                ct.ThrowIfCancellationRequested();
                expanded += count;
                if (expanded > FormCatalogImportLimits.MaximumArchiveBytes) return FormCatalogImportErrors.FileTooLarge;
            }
        }
        return Result.Success();
    }
}
