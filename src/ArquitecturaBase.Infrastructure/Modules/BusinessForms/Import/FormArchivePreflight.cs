using System.IO.Compression;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Import;

// The ZIP preflight of a workbook and of a Word, before a library opens them: every entry once, within MaximumArchiveBytes by the
// sizes declared and by the bytes ZipArchive delivers (in .NET 10 a forged shorter length truncates the delivery to it, so the
// central directory is not read again). Nothing is extracted to disk.
internal static class FormArchivePreflight
{
    public static Result Check(ZipArchive archive, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(archive);
        long declared = 0; long expanded = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        var buffer = new byte[81920];
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (!names.Add(entry.FullName)) return FormCatalogImportErrors.FileUnreadable;
            if (entry.Length > FormCatalogImportLimits.MaximumArchiveBytes - declared) return FormCatalogImportErrors.FileTooLarge;
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
