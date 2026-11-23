using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Files;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Documents;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using SkiaSharp;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// The reading of documents (Parte L): a real file of each case through the port and the document reader, without a database. What
// the file is comes from its bytes, never from the extension, and every rejection says what file it was. What a reading allocates
// is measured on the whole process, so the class runs alone: with other tests at the same time, their allocations counted as its.
[Collection(AloneTestGroup.Name)]
public sealed class FormDocumentAttachmentTests
{
    [Theory]
    [InlineData("pdf", FormDocumentFormat.Pdf)]
    [InlineData("jpeg", FormDocumentFormat.Jpeg)]
    [InlineData("png", FormDocumentFormat.Png)]
    [InlineData("docx", FormDocumentFormat.Docx)]
    [InlineData("docm", FormDocumentFormat.WordMacroEnabled)]
    [InlineData("doc", FormDocumentFormat.WordLegacy)]
    [InlineData("xlsx", FormDocumentFormat.Unknown)]
    [InlineData("xlsm", FormDocumentFormat.Unknown)]
    [InlineData("encrypted", FormDocumentFormat.Unknown)]
    [InlineData("zip", FormDocumentFormat.Unknown)]
    [InlineData("csv", FormDocumentFormat.Unknown)]
    public async Task The_format_comes_from_the_bytes(string sample, FormDocumentFormat format)
    {
        await using var services = new ServiceCollection().AddBusinessFormsInfrastructure().BuildServiceProvider();

        Assert.Equal(format, services.GetRequiredService<IFormDocumentReader>().DetectFormat(Sample(sample)));
    }

    [Fact]
    public async Task A_pdf_of_one_page_is_prepared_with_its_pages_whatever_its_name()
    {
        var bytes = FormDocumentFixture.Pdf(1);

        var document = await PreparedAsync(bytes, "C:\\fakepath\\comprobante.png");

        Assert.Equal(("comprobante.png", StructuredDocumentKind.Pdf, "application/pdf", (int?)1, (StructuredDocumentReading?)null),
            (document.FileName, document.Kind, document.MediaType, document.Pages, document.Reading));
        Assert.Equal((bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes))), (document.Size, document.Sha256));
    }

    [Fact]
    public async Task A_pdf_of_eleven_pages_has_too_many()
    {
        Assert.Equal(10, (await PreparedAsync(FormDocumentFixture.Pdf(10), "resumen.pdf")).Pages);

        await RejectedAsync(FormDocumentFixture.Pdf(11), "resumen.pdf", StructuredAttachmentRejection.TooManyPages, StructuredAttachmentFileKind.Pdf);
    }

    [Fact]
    public async Task A_pdf_without_pages_or_that_does_not_open_is_unreadable()
    {
        var whole = FormDocumentFixture.Pdf(1);

        await RejectedAsync(FormDocumentFixture.PdfWithoutPages(), "vacio.pdf", StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Pdf);
        await RejectedAsync("%PDF-1.7\nno es un documento"u8.ToArray(), "roto.pdf", StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Pdf);
        await RejectedAsync(whole[..(whole.Length / 2)], "cortado.pdf", StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Pdf);
    }

    // PdfPig follows the page tree recursively: a chain just under the object limit opens on the reader's own thread with a large
    // stack (on a thread of the pool it would end the process), and one over it is unreadable before PdfPig ever sees it, although
    // PdfPig would open it on that stack.
    [Fact]
    public async Task A_page_tree_nested_just_under_the_object_limit_opens_without_ending_the_process()
    {
        var document = await PreparedAsync(FormDocumentFixture.NestedPages(FormDocumentReader.MaximumPdfObjects - 1_000), "profundo.pdf");

        Assert.Equal(1, document.Pages);
    }

    [Fact]
    public async Task A_page_tree_over_the_object_limit_is_unreadable_before_opening_it()
    {
        await RejectedAsync(FormDocumentFixture.NestedPages(FormDocumentReader.MaximumPdfObjects), "profundo.pdf", StructuredAttachmentRejection.Unreadable,
            StructuredAttachmentFileKind.Pdf);
    }

    // Inside compressed object streams the objects are not visible in the bytes: the /N each stream declares counts them.
    [Theory]
    [InlineData(FormDocumentReader.MaximumPdfObjects - 1_000, true)]
    [InlineData(FormDocumentReader.MaximumPdfObjects, false)]
    public async Task A_page_tree_in_object_streams_counts_the_objects_they_declare(int depth, bool opens)
    {
        var preparation = await FormDocumentFixture.PrepareAsync(FormDocumentFixture.NestedPagesInObjectStreams(depth), "profundo.pdf");

        if (opens) Assert.Equal(1, Assert.IsType<StructuredDocument>(preparation.Document).Pages);
        else Assert.Equal((StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Pdf), (preparation.Rejection, preparation.FileKind));
    }

    // What is prepared here is stored later with a response: the private files accept and refuse the same PDF and Word.
    [Theory]
    [InlineData("pdf", true)]
    [InlineData("pdf-without-its-end-in-the-last-kib", false)]
    [InlineData("word-declared-as-word-writes-it", true)]
    [InlineData("word-declared-only-by-default", false)]
    public async Task A_document_is_prepared_only_when_the_private_files_would_store_it(string sample, bool accepted)
    {
        const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        var (bytes, mediaType) = sample switch
        {
            "pdf" => (FormDocumentFixture.Pdf(1), "application/pdf"),
            "pdf-without-its-end-in-the-last-kib" => (FormDocumentFixture.Pdf(1).Concat(new byte[2048]).ToArray(), "application/pdf"),
            "word-declared-as-word-writes-it" => (FormDocumentFixture.Word(FormDocumentFixture.Paragraph("Contrato")), Docx),
            _ => (FormDocumentFixture.Word(main => main.Document = new W.Document(new W.Body(FormDocumentFixture.Paragraph("Contrato"))),
                declareMainPart: false), Docx),
        };

        var preparation = await FormDocumentFixture.PrepareAsync(bytes, "documento");
        var stored = new FileContentProcessor().Process(bytes, mediaType, Convert.ToHexStringLower(SHA256.HashData(bytes)),
            StructuredAttachmentLimits.MaximumBytes, imageOnly: false);

        Assert.Equal((accepted, accepted), (preparation.Document is not null, stored.IsSuccess));
        if (!accepted) Assert.Equal(StructuredAttachmentRejection.Unreadable, preparation.Rejection);
    }

    [Fact]
    public async Task A_pdf_that_asks_for_a_password_to_open_is_protected()
    {
        await RejectedAsync(FormDocumentFixture.Committed("opening-password.pdf"), "resumen.pdf", StructuredAttachmentRejection.Protected,
            StructuredAttachmentFileKind.Pdf);
    }

    [Fact]
    public async Task A_pdf_that_only_restricts_permissions_opens_and_is_prepared()
    {
        var document = await PreparedAsync(FormDocumentFixture.Committed("owner-permissions.pdf"), "resumen.pdf");

        Assert.Equal((StructuredDocumentKind.Pdf, (int?)1), (document.Kind, document.Pages));
    }

    [Theory]
    [InlineData("jpeg", "foto.png", StructuredDocumentKind.Jpeg, "image/jpeg")]
    [InlineData("png", "foto.jpg", StructuredDocumentKind.Png, "image/png")]
    public async Task A_photo_is_prepared_by_its_bytes(string sample, string name, StructuredDocumentKind kind, string mediaType)
    {
        var document = await PreparedAsync(Sample(sample), name);

        Assert.Equal((name, kind, mediaType, (int?)null, (StructuredDocumentReading?)null),
            (document.FileName, document.Kind, document.MediaType, document.Pages, document.Reading));
    }

    // The limits of the photos of the forms, as the header declares them: 16.384 pixels per side and 32 Mi pixels in all.
    [Theory]
    [InlineData("jpeg-20000-wide")]
    [InlineData("jpeg-36-megapixels")]
    [InlineData("png-20000-wide")]
    public async Task A_photo_over_the_limits_is_too_large_without_decoding_it(string sample)
    {
        var bytes = sample switch
        {
            "jpeg-20000-wide" => FormDocumentFixture.DeclaredJpeg(20_000, 10),
            "jpeg-36-megapixels" => FormDocumentFixture.DeclaredJpeg(6_000, 6_000),
            _ => FormDocumentFixture.Photo(SKEncodedImageFormat.Png, 20_000, 1),
        };

        await RejectedAsync(bytes, "foto.jpg", StructuredAttachmentRejection.TooLarge, StructuredAttachmentFileKind.Image);
    }

    [Fact]
    public async Task A_cut_photo_is_unreadable()
    {
        var jpeg = FormDocumentFixture.Photo(SKEncodedImageFormat.Jpeg, 16, 16);

        await RejectedAsync(jpeg[..20], "foto.jpg", StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Image);
    }

    // The body in order, tables as Markdown rows: no header, footer or comment, no deleted text, empty lines left out, a pipe escaped
    // in a cell, the paragraphs of a cell joined and a table inside a cell flattened into it.
    [Fact]
    public async Task A_word_gives_its_paragraphs_and_tables_in_order_as_text()
    {
        var bytes = FormDocumentFixture.Word(main =>
        {
            var header = main.AddNewPart<HeaderPart>();
            header.Header = new W.Header(FormDocumentFixture.Paragraph("Encabezado privado"));
            var footer = main.AddNewPart<FooterPart>();
            footer.Footer = new W.Footer(FormDocumentFixture.Paragraph("Pie privado"));
            var comments = main.AddNewPart<WordprocessingCommentsPart>();
            comments.Comments = new W.Comments(new W.Comment(FormDocumentFixture.Paragraph("Comentario privado")) { Id = "0", Author = "Ficticio" });
            main.Document = new W.Document(new W.Body(
                FormDocumentFixture.Paragraph("Comprobante de transferencia"),
                new W.Paragraph(),
                new W.Paragraph(new W.Run(new W.Text("Monto:")), new W.Run(new W.TabChar(), new W.Text("1.500,25"))),
                new W.Paragraph(new W.Run(new W.DeletedText("texto borrado")), new W.Run(new W.Text("Concepto:  alquiler"))),
                new W.Table(
                    FormDocumentFixture.Row("Banco", "CBU"),
                    FormDocumentFixture.Row("Banco | Ficticio", "0000003100010000000001"),
                    FormDocumentFixture.Row("", ""),
                    new W.TableRow(
                        FormDocumentFixture.Cell(FormDocumentFixture.Paragraph("Notas"), FormDocumentFixture.Paragraph("en dos líneas")),
                        FormDocumentFixture.Cell(new W.Table(FormDocumentFixture.Row("anidada", "tabla")), new W.Paragraph()))),
                FormDocumentFixture.Paragraph("Fin del documento"),
                new W.SectionProperties(
                    new W.HeaderReference { Type = W.HeaderFooterValues.Default, Id = main.GetIdOfPart(header) },
                    new W.FooterReference { Type = W.HeaderFooterValues.Default, Id = main.GetIdOfPart(footer) })));
        });

        var document = await PreparedAsync(bytes, "contrato.pdf");

        Assert.Equal((StructuredDocumentKind.Docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", (int?)null),
            (document.Kind, document.MediaType, document.Pages));
        var reading = Assert.IsType<StructuredDocumentReading>(document.Reading);
        Assert.Equal((StructuredDocumentReadability.Readable, (StructuredDocumentProblem?)null, (string?)null, false),
            (reading.Readability, reading.Problem, reading.Language, reading.Truncated));
        Assert.Equal(string.Join('\n',
            "Comprobante de transferencia",
            "Monto: 1.500,25",
            "Concepto: alquiler",
            "| Banco | CBU |",
            "| Banco \\| Ficticio | 0000003100010000000001 |",
            "| Notas en dos líneas | anidada tabla |",
            "Fin del documento"), reading.Text);
    }

    [Fact]
    public async Task A_long_word_is_cut_at_eight_thousand_characters()
    {
        var paragraphs = Enumerable.Range(1, 60).Select(index => FormDocumentFixture.Paragraph(index + " " + new string('x', 200))).ToArray();

        var reading = (await PreparedAsync(FormDocumentFixture.Word(paragraphs), "largo.docx")).Reading!;

        Assert.Equal((StructuredAttachmentLimits.MaximumDocumentCharacters, true), (reading.Text.Length, reading.Truncated));
        Assert.StartsWith("1 xxx", reading.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_word_without_text_is_empty()
    {
        var onlyBlanks = FormDocumentFixture.Word(main => main.Document = new W.Document(new W.Body(new W.Paragraph(),
            new W.Table(FormDocumentFixture.Row(" ", "")), FormDocumentFixture.Paragraph("   "))));

        await RejectedAsync(FormDocumentFixture.Word(), "vacio.docx", StructuredAttachmentRejection.Empty, StructuredAttachmentFileKind.Word);
        await RejectedAsync(onlyBlanks, "vacio.docx", StructuredAttachmentRejection.Empty, StructuredAttachmentFileKind.Word);
    }

    // Smart tags are kept although the library does not type their content, the origin of a tracked move is not repeated, a text box
    // inside a paragraph is set apart, only the first choice of an alternate content counts and math stays out.
    [Fact]
    public async Task A_word_keeps_smart_tags_and_text_boxes_once_and_leaves_moves_and_math_out()
    {
        var bytes = FormDocumentFixture.RawWord(
            "<w:p><w:r><w:t xml:space=\"preserve\">Fecha: </w:t></w:r><w:smartTag w:element=\"date\"><w:r><w:t>11/11/2026</w:t></w:r></w:smartTag></w:p>"
            + "<w:p><w:moveFrom><w:r><w:t>origen</w:t></w:r></w:moveFrom><w:moveTo><w:r><w:t>destino</w:t></w:r></w:moveTo></w:p>"
            + "<w:p><w:r><w:t>antes</w:t></w:r><w:r><w:pict><v:shape><v:textbox><w:txbxContent><w:p><w:r><w:t>caja</w:t></w:r></w:p>"
            + "</w:txbxContent></v:textbox></v:shape></w:pict></w:r><w:r><w:t>despues</w:t></w:r></w:p>"
            + "<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:t>uno</w:t></mc:Choice><mc:Choice Requires=\"w\"><w:t>dos</w:t></mc:Choice>"
            + "<mc:Fallback><w:t>tres</w:t></mc:Fallback></mc:AlternateContent></w:r></w:p>"
            + "<w:p><m:oMath><m:r><m:t>x=2</m:t></m:r></m:oMath><w:r><w:t>fórmula</w:t></w:r></w:p>");

        var reading = (await PreparedAsync(bytes, "contrato.docx")).Reading!;

        Assert.Equal("Fecha: 11/11/2026\ndestino\nantes caja despues\nuno\nfórmula", reading.Text);
    }

    // Exactly the limit is not truncated when what follows adds nothing to the text.
    [Theory]
    [InlineData("<w:p/>", false)]
    [InlineData("<w:p><w:r><w:t xml:space=\"preserve\"> </w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl>", false)]
    [InlineData("<w:p><w:r><w:t>q</w:t></w:r></w:p>", true)]
    public async Task Exactly_eight_thousand_characters_are_truncated_only_by_more_text(string after, bool truncated)
    {
        var limit = new string('x', StructuredAttachmentLimits.MaximumDocumentCharacters);

        var reading = (await PreparedAsync(FormDocumentFixture.RawWord($"<w:p><w:r><w:t>{limit}</w:t></w:r></w:p>{after}"), "contrato.docx")).Reading!;

        Assert.Equal((limit, truncated), (reading.Text, reading.Truncated));
    }

    // A truncated relationships part, a DTD in the package relationships or a main part that is not there throw from the library:
    // the Word does not open, and nothing escapes as a failure of the server.
    [Theory]
    [InlineData("cut-document-relationships")]
    [InlineData("package-relationships-with-a-dtd")]
    [InlineData("main-part-missing")]
    public async Task A_word_whose_relationships_do_not_open_is_unreadable(string sample)
    {
        const string Body = "<w:p><w:r><w:t>Hola</w:t></w:r></w:p>";
        const string Relationships = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">";
        const string MainRelationship = "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=";
        var bytes = sample switch
        {
            "cut-document-relationships" => FormDocumentFixture.RawWord(Body, documentRelationships: Relationships + "<Relationship Id="),
            "package-relationships-with-a-dtd" => FormDocumentFixture.RawWord(Body,
                relationships: "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY a \"b\">]>" + Relationships + MainRelationship + "\"word/document.xml\"/></Relationships>"),
            _ => FormDocumentFixture.RawWord(Body, relationships: Relationships + MainRelationship + "\"word/nothere.xml\"/></Relationships>"),
        };

        await RejectedAsync(bytes, "contrato.docx", StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Word);
    }

    // F6: a few hundred KiB that inflate to half a GiB, in the cross-reference stream or in an object stream, are unreadable without
    // being inflated: the reader stops at its budget, long before PdfPig would have held the whole stream.
    [Theory]
    [InlineData("references")]
    [InlineData("object-stream")]
    [InlineData("references-ascii85")]
    [InlineData("references-hex")]
    public async Task A_decompression_bomb_is_unreadable_without_inflating_it(string where)
    {
        var bytes = where switch
        {
            "references" => FormHostilePdfFixture.BombInReferences(),
            "references-ascii85" => FormHostilePdfFixture.BombInReferences("ascii85"),
            "references-hex" => FormHostilePdfFixture.BombInReferences("hex"),
            _ => FormHostilePdfFixture.BombInObjectStream(),
        };
        Assert.True(bytes.Length < StructuredAttachmentLimits.MaximumBytes);
        var reader = Reader();
        var watch = Stopwatch.StartNew();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var content = await reader.ReadAsync(bytes, FormDocumentFormat.Pdf, TestContext.Current.CancellationToken);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.Equal(FormDocumentReadStatus.Unreadable, content.Status);
        Assert.InRange(allocated, 0, 192L * 1024 * 1024);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20));
        await RejectedAsync(bytes, "bomba.pdf", StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Pdf);
    }

    // A reading that takes longer than its time is unreadable and gives its place back, although its thread goes on; with two of them
    // still running, a new PDF is unreadable without a third thread being opened, and once they end the reader opens again (F6).
    [Fact]
    public async Task A_pdf_that_takes_too_long_is_unreadable_and_with_two_still_running_a_third_is_not_opened()
    {
        using var release = new ManualResetEventSlim();
        var opened = 0;
        var logger = new FakeLogger<FormDocumentReader>();
        var reader = new FormDocumentReader(logger, TimeSpan.FromMilliseconds(300), _ =>
        {
            Interlocked.Increment(ref opened);
            release.Wait(TimeSpan.FromSeconds(60));
            return new(FormDocumentReadStatus.Read, Pages: 1);
        });
        var watch = Stopwatch.StartNew();
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
                Assert.Equal(FormDocumentReadStatus.Unreadable,
                    (await reader.ReadAsync(FormDocumentFixture.Pdf(1), FormDocumentFormat.Pdf, TestContext.Current.CancellationToken)).Status);
            Assert.Equal(FormDocumentReader.MaximumAbandonedPdfs, Volatile.Read(ref opened));
        }
        finally
        {
            release.Set();
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20));
        Assert.Equal(3, logger.Collector.GetSnapshot().Count(record => record.Level == LogLevel.Warning));
        // The abandoned threads end and the reader is back.
        var back = false;
        for (var wait = 0; wait < 50 && !back; wait++)
        {
            back = (await reader.ReadAsync(FormDocumentFixture.Pdf(1), FormDocumentFormat.Pdf, TestContext.Current.CancellationToken)).Status == FormDocumentReadStatus.Read;
            if (!back) await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        Assert.True(back);
    }

    // A PDF whose standard security handler does not open with the empty password (here a made-up one) is Protected before PdfPig
    // opens it.
    [Fact]
    public async Task A_pdf_with_an_encrypt_entry_is_protected_without_being_opened()
    {
        var reader = new FormDocumentReader(NullLogger<FormDocumentReader>.Instance, FormDocumentReader.PdfReadTimeout,
            _ => throw new InvalidOperationException("PdfPig must not open it."));

        var content = await reader.ReadAsync(FormHostilePdfFixture.Encrypted(), FormDocumentFormat.Pdf, TestContext.Current.CancellationToken);

        Assert.Equal(FormDocumentReadStatus.Protected, content.Status);
    }

    // A PDF encrypted only for permissions (an empty opening password) is measured after decrypting it with that password, whatever
    // the scheme of the standard security handler: the bomb in its object stream is unreadable without being inflated.
    [Theory]
    [InlineData("rc4-40")]
    [InlineData("rc4-128")]
    [InlineData("aes-128")]
    [InlineData("aes-256")]
    public async Task A_decompression_bomb_in_a_pdf_encrypted_for_permissions_only_is_unreadable(string scheme)
    {
        var bytes = FormHostilePdfFixture.EncryptedObjectStream(scheme, bomb: true);
        Assert.True(bytes.Length < StructuredAttachmentLimits.MaximumBytes);
        var watch = Stopwatch.StartNew();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var content = await Reader().ReadAsync(bytes, FormDocumentFormat.Pdf, TestContext.Current.CancellationToken);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.Equal(FormDocumentReadStatus.Unreadable, content.Status);
        Assert.InRange(allocated, 0, 192L * 1024 * 1024);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20));
    }

    [Theory]
    [InlineData("rc4-40")]
    [InlineData("rc4-128")]
    [InlineData("aes-128")]
    [InlineData("aes-256")]
    public async Task A_pdf_encrypted_for_permissions_only_is_read_whatever_its_scheme(string scheme)
    {
        var content = await Reader().ReadAsync(FormHostilePdfFixture.EncryptedObjectStream(scheme, bomb: false), FormDocumentFormat.Pdf,
            TestContext.Current.CancellationToken);

        Assert.Equal((FormDocumentReadStatus.Read, 1), (content.Status, content.Pages));
    }

    [Theory]
    [InlineData("rc4-40")]
    [InlineData("rc4-128")]
    [InlineData("aes-128")]
    [InlineData("aes-256")]
    public async Task A_pdf_encrypted_with_an_opening_password_is_protected_without_being_opened(string scheme)
    {
        var reader = new FormDocumentReader(NullLogger<FormDocumentReader>.Instance, FormDocumentReader.PdfReadTimeout,
            _ => throw new InvalidOperationException("PdfPig must not open it."));

        var content = await reader.ReadAsync(FormHostilePdfFixture.EncryptedObjectStream(scheme, bomb: true, openingPassword: "secreto"),
            FormDocumentFormat.Pdf, TestContext.Current.CancellationToken);

        Assert.Equal(FormDocumentReadStatus.Protected, content.Status);
    }

    // F6: ASCII85 or hexadecimal before Flate (ReportLab and others) is measured by decoding, not refused: the PDF is read as before.
    [Theory]
    [InlineData("ascii85")]
    [InlineData("hex")]
    public async Task A_pdf_with_text_encoding_before_flate_is_read_and_measured(string encoding)
    {
        var bytes = FormHostilePdfFixture.ReadableWithEncodedReferences(encoding);
        Assert.True(FormPdfStreams.Fits(bytes, FormDocumentReader.MaximumPdfInflatedBytes, FormDocumentReader.MaximumPdfObjects));

        var content = await Reader().ReadAsync(bytes, FormDocumentFormat.Pdf, TestContext.Current.CancellationToken);

        Assert.Equal((FormDocumentReadStatus.Read, 1), (content.Status, content.Pages));
        Assert.False(FormPdfStreams.Fits(FormHostilePdfFixture.BombInReferences(encoding), FormDocumentReader.MaximumPdfInflatedBytes, FormDocumentReader.MaximumPdfObjects));
    }

    // A lack of memory is not the file's fault: it is logged, whatever is read, and the file answers unreadable.
    [Fact]
    public async Task A_lack_of_memory_reading_a_pdf_is_logged()
    {
#pragma warning disable CA2201 // The test raises what the runtime raises when the memory runs out.
        var logger = new FakeLogger<FormDocumentReader>();
        var reader = new FormDocumentReader(logger, FormDocumentReader.PdfReadTimeout, _ => throw new OutOfMemoryException());
#pragma warning restore CA2201

        var content = await reader.ReadAsync(FormDocumentFixture.Pdf(1), FormDocumentFormat.Pdf, TestContext.Current.CancellationToken);

        Assert.Equal(FormDocumentReadStatus.Unreadable, content.Status);
        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal((LogLevel.Error, typeof(OutOfMemoryException)), (record.Level, record.Exception?.GetType()));
    }

    // A Word is read up to its limits whatever it holds: deep nesting (also inside a text, where a library that loads the element
    // would recurse once per level) or millions of empty paragraphs keep the text read before them, marked truncated, without reading
    // the rest; without any text before, the Word is unreadable.
    [Theory]
    [InlineData("deep-nesting")]
    [InlineData("nested-in-text")]
    [InlineData("nested-in-tagged-text")]
    [InlineData("nested-in-field-code")]
    [InlineData("many-elements")]
    public async Task A_word_past_its_limits_keeps_what_came_before_marked_truncated(string sample)
    {
        var bytes = FormDocumentFixture.StreamedWord(target => WriteBody(target, "<w:p><w:r><w:t>Inicio</w:t></w:r></w:p>", sample));
        var reader = Reader();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var reading = reader.ReadAsync(bytes, FormDocumentFormat.Docx, TestContext.Current.CancellationToken);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var content = await reading;

        Assert.Equal((FormDocumentReadStatus.Read, "Inicio", true), (content.Status, content.Text, content.Truncated));
        Assert.InRange(allocated, 0, 96L * 1024 * 1024);
        var prepared = (await PreparedAsync(bytes, "contrato.docx")).Reading!;
        Assert.Equal(("Inicio", true), (prepared.Text, prepared.Truncated));
    }

    // One text larger than MaximumWordBytes is read up to the cut: the document keeps its first characters, marked truncated, and
    // reading it allocates a bounded amount.
    [Fact]
    public async Task A_huge_text_keeps_its_first_characters_marked_truncated()
    {
        var bytes = FormDocumentFixture.StreamedWord(target => WriteBody(target, "<w:p><w:r><w:t>Inicio</w:t></w:r></w:p>", "giant-text"));
        var reader = Reader();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var reading = reader.ReadAsync(bytes, FormDocumentFormat.Docx, TestContext.Current.CancellationToken);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var content = await reading;

        Assert.Equal((FormDocumentReadStatus.Read, true), (content.Status, content.Truncated));
        Assert.StartsWith("Inicio\nAAAA", content.Text);
        Assert.InRange(allocated, 0, 96L * 1024 * 1024);
        var prepared = (await PreparedAsync(bytes, "contrato.docx")).Reading!;
        var expected = "Inicio\n" + new string('A', StructuredAttachmentLimits.MaximumDocumentCharacters - "Inicio\n".Length);
        Assert.Equal((expected, true), (prepared.Text, prepared.Truncated));
    }

    [Theory]
    [InlineData("deep-nesting")]
    [InlineData("nested-in-text")]
    [InlineData("many-elements")]
    public async Task A_word_past_its_limits_without_text_before_is_unreadable(string sample)
    {
        var bytes = FormDocumentFixture.StreamedWord(target => WriteBody(target, "", sample));

        await RejectedAsync(bytes, "contrato.docx", StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Word);
    }

    [Fact]
    public async Task A_word_with_macros_is_rejected_by_its_manifest()
    {
        await RejectedAsync(Sample("docm"), "contrato.docx", StructuredAttachmentRejection.MacroEnabled, StructuredAttachmentFileKind.Word);
    }

    [Fact]
    public async Task A_word_older_than_2007_is_legacy()
    {
        await RejectedAsync(FormSpreadsheetFixture.Compound("WordDocument"), "contrato.xls", StructuredAttachmentRejection.Legacy,
            StructuredAttachmentFileKind.Word);
    }

    [Fact]
    public async Task A_word_whose_xml_brings_a_dtd_or_is_cut_is_unreadable()
    {
        const string Namespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var word = FormDocumentFixture.Word(FormDocumentFixture.Paragraph("Contrato"));
        var withDtd = FormDocumentFixture.WithDocumentXml(word, "<?xml version=\"1.0\"?><!DOCTYPE w:document [<!ENTITY x \"y\">]>"
            + $"<w:document xmlns:w=\"{Namespace}\"><w:body><w:p><w:r><w:t>&x;</w:t></w:r></w:p></w:body></w:document>");
        var cut = FormDocumentFixture.WithDocumentXml(word, $"<w:document xmlns:w=\"{Namespace}\"><w:body><w:p><w:r><w:t>Contrato");

        await RejectedAsync(withDtd, "contrato.docx", StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Word);
        await RejectedAsync(cut, "contrato.docx", StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Word);
    }

    [Fact]
    public async Task A_word_over_the_limits_of_the_archive_is_unreadable_before_opening_it()
    {
        var word = FormDocumentFixture.Word(FormDocumentFixture.Paragraph("Contrato"));
        var expanded = FormSpreadsheetFixture.ArchiveExpansion(word, FormCatalogImportLimits.MaximumArchiveBytes + 1, forgedLength: false);

        await RejectedAsync(expanded, "contrato.docx", StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Word);
    }

    [Fact]
    public async Task A_zip_that_is_not_a_document_nor_a_spreadsheet_is_unreadable()
    {
        await RejectedAsync(FormSpreadsheetFixture.Zip("notas.txt", "fotos/1.bin"), "contrato.docx", StructuredAttachmentRejection.Unreadable,
            StructuredAttachmentFileKind.Unknown);
    }

    // An OLE2 container with a password does not say whether it was a Word or an Excel: the extension names it, only for the text.
    [Theory]
    [InlineData("contrato.docx", StructuredAttachmentFileKind.Word)]
    [InlineData("precios.xlsx", StructuredAttachmentFileKind.Spreadsheet)]
    [InlineData("archivo.bin", StructuredAttachmentFileKind.Unknown)]
    public async Task A_file_with_a_password_is_named_by_its_extension(string name, StructuredAttachmentFileKind kind)
    {
        await RejectedAsync(FormSpreadsheetFixture.Compound("EncryptionInfo", "EncryptedPackage"), name, StructuredAttachmentRejection.Protected, kind);
    }

    private static byte[] Sample(string sample) => sample switch
    {
        "pdf" => FormDocumentFixture.Pdf(1),
        "jpeg" => FormDocumentFixture.Photo(SKEncodedImageFormat.Jpeg, 32, 24),
        "png" => FormDocumentFixture.Photo(SKEncodedImageFormat.Png, 32, 24),
        "docx" => FormDocumentFixture.Word(FormDocumentFixture.Paragraph("Contrato")),
        "docm" => FormDocumentFixture.Word(main => main.Document = new W.Document(new W.Body(FormDocumentFixture.Paragraph("Contrato"))),
            WordprocessingDocumentType.MacroEnabledDocument),
        "doc" => FormSpreadsheetFixture.Compound("WordDocument"),
        "xlsx" => FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Código"),
        "xlsm" => FormSpreadsheetFixture.MacroEnabled(FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Código")),
        "encrypted" => FormSpreadsheetFixture.Compound("EncryptionInfo", "EncryptedPackage"),
        "zip" => FormSpreadsheetFixture.Zip("notas.txt"),
        "csv" => FormSpreadsheetFixture.Csv("Código;Descripción\n1;Café\n"),
        _ => throw new ArgumentOutOfRangeException(nameof(sample)),
    };

    // A body with its first content and then, written in pieces, what passes one limit: a text of 50 MiB (more than MaximumWordBytes
    // and within the preflight of the archive), more levels than MaximumWordDepth with a paragraph at the bottom, a thousand times
    // those levels inside a text (of a run, of a smart tag or of a field code), or more empty paragraphs than MaximumWordElements
    // with one at the end.
    private static void WriteBody(Stream target, string first, string sample)
    {
        void Write(string text) => target.Write(Encoding.UTF8.GetBytes(text));
        Write($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"{FormDocumentFixture.WordNamespace}\"><w:body>{first}");
        switch (sample)
        {
            case "giant-text":
                Write("<w:p><w:r><w:t>");
                var letters = Encoding.ASCII.GetBytes(new string('A', 1024 * 1024));
                for (var mebibyte = 0; mebibyte < 50; mebibyte++) target.Write(letters);
                Write("</w:t></w:r></w:p>");
                break;
            case "deep-nesting":
                var levels = FormDocumentReader.MaximumWordDepth * 4;
                for (var level = 0; level < levels; level++) Write("<w:sdt><w:sdtContent>");
                Write("<w:p><w:r><w:t>hondo</w:t></w:r></w:p>");
                for (var level = 0; level < levels; level++) Write("</w:sdtContent></w:sdt>");
                break;
            case "nested-in-text" or "nested-in-tagged-text" or "nested-in-field-code":
                var (open, close) = sample switch
                {
                    "nested-in-text" => ("<w:p><w:r><w:t>", "</w:t></w:r></w:p>"),
                    "nested-in-tagged-text" => ("<w:p><w:smartTag w:uri=\"u\" w:element=\"e\"><w:r><w:t>", "</w:t></w:r></w:smartTag></w:p>"),
                    _ => ("<w:p><w:r><w:instrText>", "</w:instrText></w:r></w:p>"),
                };
                var inner = FormDocumentReader.MaximumWordDepth * 1000;
                Write(open);
                for (var level = 0; level < inner; level++) Write("<x>");
                Write("hondo");
                for (var level = 0; level < inner; level++) Write("</x>");
                Write(close);
                break;
            default:
                var empty = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("<w:p/>", 1000)));
                for (var thousand = 0; thousand < FormDocumentReader.MaximumWordElements / 1000 * 2; thousand++) target.Write(empty);
                Write("<w:p><w:r><w:t>fin</w:t></w:r></w:p>");
                break;
        }
        Write("</w:body></w:document>");
    }

    private static async Task<StructuredDocument> PreparedAsync(byte[] bytes, string name)
    {
        var preparation = await FormDocumentFixture.PrepareAsync(bytes, name);
        Assert.Null(preparation.Rejection);
        return Assert.IsType<StructuredDocument>(preparation.Document);
    }

    private static async Task RejectedAsync(byte[] bytes, string name, StructuredAttachmentRejection rejection, StructuredAttachmentFileKind kind)
    {
        var preparation = await FormDocumentFixture.PrepareAsync(bytes, name);
        Assert.Equal((rejection, kind), (preparation.Rejection, preparation.FileKind));
        Assert.Null(preparation.Document);
        Assert.Null(preparation.Spreadsheet);
    }

    private static FormDocumentReader Reader() => new(NullLogger<FormDocumentReader>.Instance);
}
