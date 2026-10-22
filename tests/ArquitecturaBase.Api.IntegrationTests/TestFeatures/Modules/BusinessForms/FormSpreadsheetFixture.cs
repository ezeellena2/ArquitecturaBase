using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal static class FormSpreadsheetFixture
{
    public static async Task<Result<FormSpreadsheet>> ReadAsync(byte[] bytes, string name = "options.xlsx")
    {
        using var content = new MemoryStream(bytes);
        return await ReadAsync(content, name);
    }

    public static async Task<Result<FormSpreadsheet>> ReadAsync(Stream content, string name, CancellationToken? cancellationToken = null)
    {
        await using var services = new ServiceCollection().AddBusinessFormsInfrastructure().BuildServiceProvider();
        return await services.GetRequiredService<IFormSpreadsheetReader>().ReadAsync(content, name, cancellationToken ?? TestContext.Current.CancellationToken);
    }

    public static byte[] Workbook(Action<IXLWorksheet> configure, bool second = false)
    {
        using var workbook = new XLWorkbook();
        configure(workbook.AddWorksheet("Options"));
        if (second) workbook.AddWorksheet("Ignored").Cell(1, 1).Value = "secondary";
        using var content = new MemoryStream();
        workbook.SaveAs(content, new ClosedXML.Excel.SaveOptions { EvaluateFormulasBeforeSaving = false });
        return content.ToArray();
    }

    public static byte[] Sheet(byte[] workbook, string xml, int number = 1)
    {
        using var content = new MemoryStream();
        content.Write(workbook);
        using (var archive = new ZipArchive(content, ZipArchiveMode.Update, true))
        {
            var entry = archive.GetEntry($"xl/worksheets/sheet{number}.xml")!;
            XDocument document;
            using (var source = entry.Open()) document = XDocument.Load(source);
            var ns = document.Root!.Name.Namespace;
            document.Root.Element(ns + "sheetData")!.ReplaceWith(XElement.Parse(xml));
            entry.Delete();
            using var target = archive.CreateEntry($"xl/worksheets/sheet{number}.xml").Open();
            document.Save(target);
        }
        return content.ToArray();
    }

    public static byte[] ArchiveExpansion(byte[] workbook, int expandedBytes, bool forgedLength)
    {
        using var content = new MemoryStream();
        content.Write(workbook);
        using (var archive = new ZipArchive(content, ZipArchiveMode.Update, true))
        {
            using var target = archive.CreateEntry("xl/media/large.bin", CompressionLevel.SmallestSize).Open();
            var zeros = new byte[81920];
            for (var remaining = expandedBytes; remaining > 0; remaining -= Math.Min(remaining, zeros.Length))
                target.Write(zeros, 0, Math.Min(remaining, zeros.Length));
        }
        var result = content.ToArray();
        if (forgedLength) DeclareLength(result, "xl/media/large.bin", 1);
        return result;
    }

    public static byte[] ConsumedArchiveExpansion(string part)
    {
        using var content = new MemoryStream();
        content.Write(Workbook(sheet => sheet.Cell(1, 1).Value = "Code"));
        using (var archive = new ZipArchive(content, ZipArchiveMode.Update, true))
        {
            archive.GetEntry(part)?.Delete();
            using var target = archive.CreateEntry(part, CompressionLevel.SmallestSize).Open();
            var worksheet = part.Contains("worksheets", StringComparison.Ordinal);
            var prefix = worksheet
                ? "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>"
                : "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"1\" uniqueCount=\"1\"><si><t>";
            var suffix = worksheet ? "</t></is></c></row></sheetData></worksheet>" : "</t></si></sst>";
            target.Write(Encoding.UTF8.GetBytes(prefix));
            var data = Enumerable.Repeat((byte)'X', 81920).ToArray();
            for (var remaining = FormCatalogImportLimits.MaximumArchiveBytes + 1; remaining > 0; remaining -= Math.Min(remaining, data.Length))
                target.Write(data, 0, Math.Min(remaining, data.Length));
            target.Write(Encoding.UTF8.GetBytes(suffix));
        }
        var result = content.ToArray();
        DeclareLength(result, part, 1);
        return result;
    }

    public static (long Declared, long Read) EffectiveEntryBytes(byte[] bytes, string part)
    {
        using var source = new MemoryStream(bytes);
        using var archive = new ZipArchive(source, ZipArchiveMode.Read);
        var entry = archive.GetEntry(part)!;
        using var data = entry.Open();
        var buffer = new byte[81920]; long read = 0; int count;
        while ((count = data.Read(buffer)) > 0)
        {
            read += count;
            if (read > FormCatalogImportLimits.MaximumArchiveBytes) break;
        }
        return (entry.Length, read);
    }

    private static void DeclareLength(byte[] bytes, string part, uint length)
    {
        // Forge only directory/local declared lengths; compressed bytes and their CRC remain real.
        for (var offset = 0; offset < bytes.Length - 46; offset++)
        {
            var signature = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
            if (signature is not (0x02014b50 or 0x04034b50)) continue;
            var header = signature == 0x02014b50 ? 46 : 30;
            var nameLengthOffset = signature == 0x02014b50 ? 28 : 26;
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + nameLengthOffset, 2));
            if (offset + header + nameLength > bytes.Length || Encoding.UTF8.GetString(bytes, offset + header, nameLength) != part) continue;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + (signature == 0x02014b50 ? 24 : 22), 4), length);
        }
    }

    public static byte[] Csv(string text, bool bom = false) => bom
        ? [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)] : Encoding.UTF8.GetBytes(text);
}

internal sealed class FormSpreadsheetInputStream(long length, bool seekable = false, byte[]? content = null) : Stream
{
    public long BytesRead { get; private set; }
    public bool WasDisposed { get; private set; }
    public override bool CanRead => !WasDisposed;
    public override bool CanSeek => seekable;
    public override bool CanWrite => false;
    public override long Length => seekable ? length : throw new NotSupportedException();
    public override long Position { get => seekable ? BytesRead : throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = (int)Math.Min(count, length - BytesRead);
        if (content is null) Array.Fill(buffer, (byte)'x', offset, read);
        else content.AsSpan((int)BytesRead, read).CopyTo(buffer.AsSpan(offset, read));
        BytesRead += read;
        return read;
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = (int)Math.Min(buffer.Length, length - BytesRead);
        if (content is null) buffer.Span[..read].Fill((byte)'x');
        else content.AsSpan((int)BytesRead, read).CopyTo(buffer.Span);
        BytesRead += read;
        return ValueTask.FromResult(read);
    }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
}
