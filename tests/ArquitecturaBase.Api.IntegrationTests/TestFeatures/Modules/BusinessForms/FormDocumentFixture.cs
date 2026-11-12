using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// Real documents for the attachment port, built in memory: PDF pages drawn by SkiaSharp or written by hand (an empty or very deep
// page tree, also inside object streams), photos it encodes (or a JPEG header that declares another size), Word files written with
// OpenXml or by hand; and the two PDF with a password committed in Fixtures, because neither SkiaSharp nor PdfPig encrypts (how
// they were made is in Fixtures/.gitattributes).
internal static class FormDocumentFixture
{
    public const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string MainType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    private const string Relationships = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>";

    // The port with the real readers of the module, without a database.
    public static async Task<StructuredAttachmentPreparation> PrepareAsync(byte[] bytes, string name)
    {
        await using var services = new ServiceCollection().AddBusinessFormsInfrastructure().BuildServiceProvider();
        var permissions = new PermissionAuthorizationFixture();
        var reader = new BusinessFormAttachmentReader(new FormCatalogAccessGuard(permissions, permissions), new FormDraftAccessGuard(permissions, permissions),
            services.GetRequiredService<IFormSpreadsheetReader>(), services.GetRequiredService<IFormDocumentReader>());
        return await reader.PrepareAsync(bytes, name, TestContext.Current.CancellationToken);
    }

    public static byte[] Committed(string name)
    {
        using var resource = typeof(FormDocumentFixture).Assembly.GetManifestResourceStream("FormDocuments." + name)
            ?? throw new InvalidOperationException("The fixture is not embedded.");
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    }

    public static byte[] Pdf(int pages)
    {
        using var output = new MemoryStream();
        using (var pdf = SKDocument.CreatePdf(output))
        {
            using var paint = new SKPaint { Color = SKColors.Black };
            for (var page = 1; page <= pages; page++)
            {
                pdf.BeginPage(200, 200).DrawRect(10, 10, 15 * page, 20, paint);
                pdf.EndPage();
            }
            pdf.Close();
        }
        return output.ToArray();
    }

    // SkiaSharp writes nothing for a PDF without pages: this one is written by hand, with an empty page tree that lies about its count.
    public static byte[] PdfWithoutPages() => HandWritten(["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [] /Count 5 >>"]);

    // A page tree of nested /Pages nodes, one inside the other, with a single page at the bottom: depth + 2 objects.
    public static byte[] NestedPages(int depth) => HandWritten(Enumerable.Range(1, depth + 2).Select(number => NestedObject(number, depth)).ToList());

    // The same chain packed in compressed object streams of 200 objects, found through a compressed cross-reference stream (PDF 1.5).
    public static byte[] NestedPagesInObjectStreams(int depth)
    {
        const int PerStream = 200;
        var total = depth + 2;
        var streams = (total + PerStream - 1) / PerStream;
        var firstStream = total + 1;
        var xrefNumber = firstStream + streams;
        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.Latin1.GetBytes(text));
        Write("%PDF-1.5\n");
        var streamOffsets = new long[streams];
        for (var stream = 0; stream < streams; stream++)
        {
            var first = stream * PerStream + 1;
            var last = Math.Min(total, first + PerStream - 1);
            var header = new StringBuilder();
            var body = new StringBuilder();
            for (var number = first; number <= last; number++)
            {
                header.Append(number).Append(' ').Append(body.Length).Append(' ');
                body.Append(NestedObject(number, depth)).Append('\n');
            }
            var packed = Deflate(Encoding.Latin1.GetBytes(header.ToString() + body));
            streamOffsets[stream] = output.Position;
            Write($"{firstStream + stream} 0 obj\n<< /Type /ObjStm /N {last - first + 1} /First {Encoding.Latin1.GetByteCount(header.ToString())} /Filter /FlateDecode /Length {packed.Length} >>\nstream\n");
            output.Write(packed);
            Write("\nendstream\nendobj\n");
        }
        using var entries = new MemoryStream();
        void Entry(byte type, long second, int third)
        {
            entries.WriteByte(type);
            for (var shift = 24; shift >= 0; shift -= 8) entries.WriteByte((byte)(second >> shift));
            entries.WriteByte((byte)(third >> 8));
            entries.WriteByte((byte)third);
        }
        Entry(0, 0, 65535);
        for (var number = 1; number <= total; number++) Entry(2, firstStream + (number - 1) / PerStream, (number - 1) % PerStream);
        foreach (var offset in streamOffsets) Entry(1, offset, 0);
        var xrefOffset = output.Position;
        Entry(1, xrefOffset, 0);
        var xref = Deflate(entries.ToArray());
        Write($"{xrefNumber} 0 obj\n<< /Type /XRef /Size {xrefNumber + 1} /W [1 4 2] /Root 1 0 R /Filter /FlateDecode /Length {xref.Length} >>\nstream\n");
        output.Write(xref);
        Write($"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    public static byte[] Photo(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 80);
        return data.ToArray();
    }

    // A small JPEG whose frame header declares another size: SkiaSharp reads only the header, and nobody decodes its pixels.
    public static byte[] DeclaredJpeg(int width, int height)
    {
        var jpeg = Photo(SKEncodedImageFormat.Jpeg, 16, 16);
        for (var index = 2; index < jpeg.Length - 9; index++)
        {
            if (jpeg[index] != 0xFF || jpeg[index + 1] is not (0xC0 or 0xC2)) continue;
            jpeg[index + 5] = (byte)(height >> 8);
            jpeg[index + 6] = (byte)height;
            jpeg[index + 7] = (byte)(width >> 8);
            jpeg[index + 8] = (byte)width;
            return jpeg;
        }
        throw new InvalidOperationException("The JPEG has no frame header.");
    }

    // A Word written with OpenXml whose manifest declares the main part as Word writes it (an Override), which the private files
    // require; OpenXml alone declares it with a Default, which they refuse.
    public static byte[] Word(Action<MainDocumentPart> write, WordprocessingDocumentType type = WordprocessingDocumentType.Document,
        bool declareMainPart = true)
    {
        byte[] written;
        using (var created = new MemoryStream())
        {
            using (var document = WordprocessingDocument.Create(created, type))
                write(document.AddMainDocumentPart());
            written = created.ToArray();
        }
        if (!declareMainPart || type != WordprocessingDocumentType.Document) return written;
        using var content = new MemoryStream();
        content.Write(written);
        using (var archive = new ZipArchive(content, ZipArchiveMode.Update, true))
        {
            var entry = archive.GetEntry("[Content_Types].xml")!;
            XDocument manifest;
            using (var source = entry.Open()) manifest = XDocument.Load(source);
            XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
            foreach (var standard in manifest.Root!.Elements(types + "Default").Where(element => (string?)element.Attribute("ContentType") == MainType))
                standard.SetAttributeValue("ContentType", "application/xml");
            if (!manifest.Root.Elements(types + "Override").Any(element => (string?)element.Attribute("PartName") == "/word/document.xml"))
                manifest.Root.Add(new XElement(types + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", MainType)));
            entry.Delete();
            using var target = archive.CreateEntry("[Content_Types].xml").Open();
            manifest.Save(target);
        }
        return content.ToArray();
    }

    public static byte[] Word(params W.Paragraph[] paragraphs) =>
        Word(main => main.Document = new W.Document(new W.Body(paragraphs)));

    public static W.Paragraph Paragraph(string text) => new(new W.Run(new W.Text(text)));

    public static W.TableCell Cell(params OpenXmlElement[] content) => new(content);

    public static W.TableRow Row(params string[] cells) => new(cells.Select(cell => Cell(Paragraph(cell))));

    // A Word written by hand around the content of its body, as Word declares it; the relationships can be replaced.
    public static byte[] RawWord(string body, string? relationships = null, string? documentRelationships = null) => StreamedWord(target =>
        target.Write(Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"{WordNamespace}\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><w:body>{body}</w:body></w:document>")),
        relationships, documentRelationships);

    // A Word whose main part is written in pieces, for one too large to hold in a string.
    public static byte[] StreamedWord(Action<Stream> writeDocument, string? relationships = null, string? documentRelationships = null)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, true))
        {
            void Entry(string name, string text)
            {
                using var target = archive.CreateEntry(name).Open();
                target.Write(Encoding.UTF8.GetBytes(text));
            }
            Entry("[Content_Types].xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"{MainType}\"/></Types>");
            Entry("_rels/.rels", relationships ?? Relationships);
            if (documentRelationships is not null) Entry("word/_rels/document.xml.rels", documentRelationships);
            using var document = archive.CreateEntry("word/document.xml").Open();
            writeDocument(document);
        }
        return content.ToArray();
    }

    // The same Word with another main part, as written by hand.
    public static byte[] WithDocumentXml(byte[] word, string xml)
    {
        using var content = new MemoryStream();
        content.Write(word);
        using (var archive = new ZipArchive(content, ZipArchiveMode.Update, true))
        {
            archive.GetEntry("word/document.xml")!.Delete();
            using var target = archive.CreateEntry("word/document.xml").Open();
            target.Write(Encoding.UTF8.GetBytes(xml));
        }
        return content.ToArray();
    }

    private static string NestedObject(int number, int depth) =>
        number == 1 ? "<< /Type /Catalog /Pages 2 0 R >>"
        : number < depth + 2 ? $"<< /Type /Pages /Kids [{number + 1} 0 R] /Count 1 >>"
        : "<< /Type /Page /MediaBox [0 0 10 10] >>";

    private static byte[] HandWritten(List<string> objects)
    {
        using var output = new MemoryStream();
        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.4\n");
        var offsets = new long[objects.Count];
        for (var index = 0; index < objects.Count; index++)
        {
            offsets[index] = output.Position;
            Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = output.Position;
        var table = new StringBuilder($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) table.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        Write(table.ToString());
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static byte[] Deflate(byte[] raw)
    {
        using var packed = new MemoryStream();
        using (var deflate = new ZLibStream(packed, CompressionLevel.SmallestSize, true)) deflate.Write(raw);
        return packed.ToArray();
    }
}
