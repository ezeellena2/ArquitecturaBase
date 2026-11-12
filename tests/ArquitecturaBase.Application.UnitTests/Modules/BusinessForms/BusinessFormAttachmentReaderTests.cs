using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Stage 2 of the assistant authoring and the reading of documents: Home and WhatsApp read a spreadsheet or a document through the
// neutral port, without naming the module. What the file is comes from its bytes; every rejection says what file it was.
public sealed class BusinessFormAttachmentReaderTests
{
    private const string Catalogs = BusinessFormsPermissions.CatalogsRead + "," + BusinessFormsPermissions.CatalogsManage;
    private const string Forms = BusinessFormsPermissions.Read + "," + BusinessFormsPermissions.Manage;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly byte[] Content = Encoding.UTF8.GetBytes("CUIT;Razón social\n30-71234567-8;Agro Sur\n");

    [Theory]
    [InlineData("", false, false)]
    [InlineData(BusinessFormsPermissions.CatalogsRead, false, false)]
    [InlineData(BusinessFormsPermissions.CatalogsManage, false, false)]
    [InlineData(Catalogs, true, false)]
    [InlineData(BusinessFormsPermissions.Read, false, false)]
    [InlineData(BusinessFormsPermissions.Manage, false, false)]
    [InlineData(Forms, false, true)]
    [InlineData(Catalogs + "," + Forms, true, true)]
    public async Task A_spreadsheet_needs_managing_catalogs_and_a_document_designing_forms(string granted, bool spreadsheets, bool documents)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = granted.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var access = await Reader(new(), new(), permissions).CanAttachAsync(actor, Ct);

        Assert.Equal(new StructuredAttachmentAccess(spreadsheets, documents), access);
    }

    [Fact]
    public async Task Who_attaches_is_the_named_account_not_the_current_user()
    {
        Guid actor = Guid.NewGuid(), other = Guid.NewGuid();
        var permissions = new FakePermissionService();
        var current = new FakeCurrentUser { UserId = other };
        permissions.Permissions[other] = (Catalogs + "," + Forms).Split(',');
        var reader = Reader(new(), new(), permissions, current);

        Assert.Equal(StructuredAttachmentAccess.None, await reader.CanAttachAsync(actor, Ct));
        permissions.Permissions[actor] = (Catalogs + "," + Forms).Split(',');
        current.UserId = null;
        Assert.Equal(new StructuredAttachmentAccess(true, true), await reader.CanAttachAsync(actor, Ct));
    }

    [Theory]
    [InlineData(FormSpreadsheetFormat.Csv, "proveedores.xlsx", "proveedores.xlsx", StructuredSpreadsheetKind.Csv, "text/csv")]
    [InlineData(FormSpreadsheetFormat.Xlsx, "C:\\fakepath\\proveedores.csv", "proveedores.csv", StructuredSpreadsheetKind.Xlsx,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task The_kind_comes_from_the_bytes_and_the_reader_reads_them_with_it(FormSpreadsheetFormat format, string name, string shown,
        StructuredSpreadsheetKind kind, string mediaType)
    {
        var spreadsheets = new Spreadsheets { Format = format };
        var documents = new Documents();

        var preparation = await Reader(spreadsheets, documents).PrepareAsync(Content, name, Ct);

        Assert.True(documents.Detected);
        Assert.Null(documents.ReadFormat);
        Assert.Equal(format, spreadsheets.ReadFormat);
        Assert.Same(Content, spreadsheets.ReadBytes);
        var prepared = preparation.Spreadsheet!;
        Assert.Equal((shown, kind, mediaType, (long)Content.Length, Spreadsheets.Sha, 2),
            (prepared.FileName, prepared.Kind, prepared.MediaType, prepared.Size, prepared.Sha256, prepared.RowCount));
        Assert.Equal(((StructuredDocument?)null, (StructuredAttachmentRejection?)null, (StructuredAttachmentFileKind?)null),
            (preparation.Document, preparation.Rejection, preparation.FileKind));
    }

    [Fact]
    public async Task The_file_name_is_bounded_at_the_border()
    {
        var preparation = await Reader(new(), new()).PrepareAsync(Content, "carpeta/" + new string('n', 300) + ".csv", Ct);

        Assert.Equal(new string('n', 119) + "…", preparation.Spreadsheet!.FileName);
    }

    [Theory]
    [InlineData(FormSpreadsheetFormat.MacroEnabled, StructuredAttachmentRejection.MacroEnabled, StructuredAttachmentFileKind.Spreadsheet)]
    [InlineData(FormSpreadsheetFormat.Legacy, StructuredAttachmentRejection.Legacy, StructuredAttachmentFileKind.Spreadsheet)]
    [InlineData(FormSpreadsheetFormat.Protected, StructuredAttachmentRejection.Protected, StructuredAttachmentFileKind.Spreadsheet)]
    [InlineData(FormSpreadsheetFormat.Unknown, StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Unknown)]
    public async Task What_the_bytes_are_rejects_before_reading_them(FormSpreadsheetFormat format, StructuredAttachmentRejection rejection,
        StructuredAttachmentFileKind kind)
    {
        var spreadsheets = new Spreadsheets { Format = format };

        var preparation = await Reader(spreadsheets, new()).PrepareAsync(Content, "proveedores.xlsx", Ct);

        Assert.Equal((rejection, kind), (preparation.Rejection, preparation.FileKind));
        Assert.Null(preparation.Spreadsheet);
        Assert.Null(spreadsheets.ReadFormat);
    }

    // An OLE2 container with a password does not say whether it was a Word or an Excel: only its name does, for the text.
    [Theory]
    [InlineData("precios.xlsx", StructuredAttachmentFileKind.Spreadsheet)]
    [InlineData("precios.xls", StructuredAttachmentFileKind.Spreadsheet)]
    [InlineData("precios.csv", StructuredAttachmentFileKind.Spreadsheet)]
    [InlineData("contrato.docx", StructuredAttachmentFileKind.Word)]
    [InlineData("C:\\fakepath\\Contrato.DOC", StructuredAttachmentFileKind.Word)]
    [InlineData("contrato.pdf", StructuredAttachmentFileKind.Unknown)]
    [InlineData("contrato", StructuredAttachmentFileKind.Unknown)]
    public async Task A_file_with_a_password_is_named_by_its_extension_only_for_the_text(string name, StructuredAttachmentFileKind kind)
    {
        var preparation = await Reader(new() { Format = FormSpreadsheetFormat.Protected }, new()).PrepareAsync(Content, name, Ct);

        Assert.Equal((StructuredAttachmentRejection.Protected, kind), (preparation.Rejection, preparation.FileKind));
    }

    [Fact]
    public async Task The_limits_of_the_import_are_too_large_and_any_other_failure_is_unreadable()
    {
        Error[] limits = [FormCatalogImportErrors.FileTooLarge, FormCatalogImportErrors.TooManyRows, FormCatalogImportErrors.TooManyColumns,
            FormCatalogImportErrors.InSheet(FormCatalogImportErrors.TooManyCells, "Hoja2")];
        foreach (var error in limits)
        {
            var tooLarge = await Reader(new() { Error = error }, new()).PrepareAsync(Content, "p.csv", Ct);
            Assert.Equal((StructuredAttachmentRejection.TooLarge, StructuredAttachmentFileKind.Spreadsheet), (tooLarge.Rejection, tooLarge.FileKind));
        }
        var unreadable = await Reader(new() { Error = FormCatalogImportErrors.FileUnreadable }, new()).PrepareAsync(Content, "p.csv", Ct);
        Assert.Equal((StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Spreadsheet), (unreadable.Rejection, unreadable.FileKind));
    }

    // Over the limit nothing is opened, so nothing says what the file was: one limit for every kind, the engine's.
    [Fact]
    public async Task Over_ten_mebibytes_is_too_large_without_opening_it()
    {
        var spreadsheets = new Spreadsheets();
        var documents = new Documents { Format = FormDocumentFormat.Pdf };

        var oversized = await Reader(spreadsheets, documents).PrepareAsync(new byte[StructuredAttachmentLimits.MaximumBytes + 1], "p.pdf", Ct);

        Assert.Equal((StructuredAttachmentRejection.TooLarge, StructuredAttachmentFileKind.Unknown), (oversized.Rejection, oversized.FileKind));
        Assert.False(spreadsheets.Detected);
        Assert.False(documents.Detected);
        Assert.Equal(StructuredAttachmentLimits.MaximumBytes, AssistantAttachmentInput.MaximumBytes);
    }

    [Fact]
    public async Task A_pdf_is_prepared_by_its_bytes_with_its_pages_and_without_a_reading()
    {
        var spreadsheets = new Spreadsheets();
        var documents = new Documents { Format = FormDocumentFormat.Pdf, Content = new(FormDocumentReadStatus.Read, Pages: 3) };

        var preparation = await Reader(spreadsheets, documents).PrepareAsync(Content, "C:\\fakepath\\comprobante.xlsx", Ct);

        var document = Assert.IsType<StructuredDocument>(preparation.Document);
        Assert.Equal(("comprobante.xlsx", StructuredDocumentKind.Pdf, "application/pdf", (long)Content.Length, Sha(Content), (int?)3),
            (document.FileName, document.Kind, document.MediaType, document.Size, document.Sha256, document.Pages));
        Assert.Null(document.Reading);
        Assert.Equal(((StructuredSpreadsheet?)null, (StructuredAttachmentRejection?)null), (preparation.Spreadsheet, preparation.Rejection));
        Assert.Equal(FormDocumentFormat.Pdf, documents.ReadFormat);
        Assert.Same(Content, documents.ReadBytes);
        Assert.False(spreadsheets.Detected);
    }

    [Theory]
    [InlineData(FormDocumentFormat.Jpeg, StructuredDocumentKind.Jpeg, "image/jpeg")]
    [InlineData(FormDocumentFormat.Png, StructuredDocumentKind.Png, "image/png")]
    public async Task A_photo_is_prepared_by_its_bytes_without_pages_or_a_reading(FormDocumentFormat format, StructuredDocumentKind kind, string mediaType)
    {
        var documents = new Documents { Format = format, Content = new(FormDocumentReadStatus.Read, Width: 1200, Height: 1600) };

        var document = (await Reader(new(), documents).PrepareAsync(Content, "foto.pdf", Ct)).Document!;

        Assert.Equal(("foto.pdf", kind, mediaType, (int?)null, (StructuredDocumentReading?)null),
            (document.FileName, document.Kind, document.MediaType, document.Pages, document.Reading));
    }

    [Fact]
    public async Task A_word_is_read_here_as_its_text()
    {
        var documents = new Documents { Format = FormDocumentFormat.Docx, Content = new(FormDocumentReadStatus.Read, Text: "Contrato\n| Monto | 1500 |") };

        var document = (await Reader(new(), documents).PrepareAsync(Content, "contrato.docx", Ct)).Document!;

        Assert.Equal((StructuredDocumentKind.Docx, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", (int?)null),
            (document.Kind, document.MediaType, document.Pages));
        Assert.Equal(new StructuredDocumentReading(StructuredDocumentReadability.Readable, null, null, "Contrato\n| Monto | 1500 |", false), document.Reading);
    }

    // The reader goes one character past the limit, so a longer text says it goes on; the cut never splits a character in two.
    [Theory]
    [InlineData(StructuredAttachmentLimits.MaximumDocumentCharacters, StructuredAttachmentLimits.MaximumDocumentCharacters, false)]
    [InlineData(StructuredAttachmentLimits.MaximumDocumentCharacters + 1, StructuredAttachmentLimits.MaximumDocumentCharacters, true)]
    [InlineData(StructuredAttachmentLimits.MaximumDocumentCharacters + 3, StructuredAttachmentLimits.MaximumDocumentCharacters, true)]
    public async Task A_word_text_is_cut_at_eight_thousand_characters_and_says_so(int length, int kept, bool truncated)
    {
        var documents = new Documents { Format = FormDocumentFormat.Docx, Content = new(FormDocumentReadStatus.Read, Text: new string('a', length)) };

        var reading = (await Reader(new(), documents).PrepareAsync(Content, "contrato.docx", Ct)).Document!.Reading!;

        Assert.Equal((kept, truncated), (reading.Text.Length, reading.Truncated));
    }

    // The reader stops before the end for its own limits (the size of the XML, its depth or its elements): the text it read stays,
    // marked truncated, without the spaces a cut leaves at the end.
    [Fact]
    public async Task A_word_the_reader_stopped_short_keeps_its_text_marked_truncated()
    {
        var documents = new Documents { Format = FormDocumentFormat.Docx, Content = new(FormDocumentReadStatus.Read, Text: "Contrato\n| Monto ", Truncated: true) };

        var reading = (await Reader(new(), documents).PrepareAsync(Content, "contrato.docx", Ct)).Document!.Reading!;

        Assert.Equal(("Contrato\n| Monto", true), (reading.Text, reading.Truncated));
    }

    [Fact]
    public async Task A_word_cut_at_the_limit_does_not_end_in_the_spaces_of_the_cut()
    {
        var text = new string('a', StructuredAttachmentLimits.MaximumDocumentCharacters - 2) + "\n b";
        var documents = new Documents { Format = FormDocumentFormat.Docx, Content = new(FormDocumentReadStatus.Read, Text: text) };

        var reading = (await Reader(new(), documents).PrepareAsync(Content, "contrato.docx", Ct)).Document!.Reading!;

        Assert.Equal((new string('a', StructuredAttachmentLimits.MaximumDocumentCharacters - 2), true), (reading.Text, reading.Truncated));
    }

    [Fact]
    public async Task The_cut_of_a_word_text_never_splits_a_character_in_two()
    {
        var text = new string('a', StructuredAttachmentLimits.MaximumDocumentCharacters - 1) + "😀b";
        var documents = new Documents { Format = FormDocumentFormat.Docx, Content = new(FormDocumentReadStatus.Read, Text: text) };

        var reading = (await Reader(new(), documents).PrepareAsync(Content, "contrato.docx", Ct)).Document!.Reading!;

        Assert.Equal((new string('a', StructuredAttachmentLimits.MaximumDocumentCharacters - 1), true), (reading.Text, reading.Truncated));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\t ")]
    public async Task A_word_without_text_is_empty(string text)
    {
        var documents = new Documents { Format = FormDocumentFormat.Docx, Content = new(FormDocumentReadStatus.Read, Text: text) };

        var preparation = await Reader(new(), documents).PrepareAsync(Content, "vacio.docx", Ct);

        Assert.Equal((StructuredAttachmentRejection.Empty, StructuredAttachmentFileKind.Word), (preparation.Rejection, preparation.FileKind));
        Assert.Null(preparation.Document);
    }

    [Theory]
    [InlineData(FormDocumentFormat.Pdf, FormDocumentReadStatus.Protected, StructuredAttachmentRejection.Protected, StructuredAttachmentFileKind.Pdf)]
    [InlineData(FormDocumentFormat.Pdf, FormDocumentReadStatus.Unreadable, StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Pdf)]
    [InlineData(FormDocumentFormat.Jpeg, FormDocumentReadStatus.Unreadable, StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Image)]
    [InlineData(FormDocumentFormat.Png, FormDocumentReadStatus.Unreadable, StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Image)]
    [InlineData(FormDocumentFormat.Docx, FormDocumentReadStatus.Unreadable, StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Word)]
    public async Task A_document_that_does_not_open_says_what_it_was(FormDocumentFormat format, FormDocumentReadStatus status,
        StructuredAttachmentRejection rejection, StructuredAttachmentFileKind kind)
    {
        var documents = new Documents { Format = format, Content = new(status) };

        var preparation = await Reader(new(), documents).PrepareAsync(Content, "archivo", Ct);

        Assert.Equal((rejection, kind), (preparation.Rejection, preparation.FileKind));
        Assert.Null(preparation.Document);
    }

    [Theory]
    [InlineData(0, StructuredAttachmentRejection.Unreadable)]
    [InlineData(StructuredAttachmentLimits.MaximumPages + 1, StructuredAttachmentRejection.TooManyPages)]
    public async Task A_pdf_without_pages_is_unreadable_and_one_over_ten_pages_too_long(int pages, StructuredAttachmentRejection rejection)
    {
        var documents = new Documents { Format = FormDocumentFormat.Pdf, Content = new(FormDocumentReadStatus.Read, Pages: pages) };

        var preparation = await Reader(new(), documents).PrepareAsync(Content, "comprobante.pdf", Ct);

        Assert.Equal((rejection, StructuredAttachmentFileKind.Pdf), (preparation.Rejection, preparation.FileKind));
        var limit = await Reader(new(), new() { Format = FormDocumentFormat.Pdf, Content = new(FormDocumentReadStatus.Read, Pages: StructuredAttachmentLimits.MaximumPages) })
            .PrepareAsync(Content, "comprobante.pdf", Ct);
        Assert.Equal(StructuredAttachmentLimits.MaximumPages, limit.Document!.Pages);
    }

    // The limits of the photos of the forms: 16.384 pixels per side and 32 Mi pixels, as the header declares them.
    [Theory]
    [InlineData(StructuredAttachmentLimits.MaximumImageSide + 1, 10, true)]
    [InlineData(10, StructuredAttachmentLimits.MaximumImageSide + 1, true)]
    [InlineData(8_000, 5_000, true)]
    [InlineData(StructuredAttachmentLimits.MaximumImageSide, 2_048, false)]
    [InlineData(StructuredAttachmentLimits.MaximumImageSide, 1, false)]
    public async Task A_photo_over_the_limits_of_the_forms_is_too_large(int width, int height, bool tooLarge)
    {
        var documents = new Documents { Format = FormDocumentFormat.Jpeg, Content = new(FormDocumentReadStatus.Read, Width: width, Height: height) };

        var preparation = await Reader(new(), documents).PrepareAsync(Content, "foto.jpg", Ct);

        if (tooLarge) Assert.Equal((StructuredAttachmentRejection.TooLarge, StructuredAttachmentFileKind.Image), (preparation.Rejection, preparation.FileKind));
        else Assert.Equal(StructuredDocumentKind.Jpeg, preparation.Document!.Kind);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    public async Task A_photo_without_a_size_is_unreadable(int width, int height)
    {
        var documents = new Documents { Format = FormDocumentFormat.Png, Content = new(FormDocumentReadStatus.Read, Width: width, Height: height) };

        var preparation = await Reader(new(), documents).PrepareAsync(Content, "foto.png", Ct);

        Assert.Equal((StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Image), (preparation.Rejection, preparation.FileKind));
    }

    // A Word with macros (.docm) or older than 2007 (.doc) is never opened.
    [Theory]
    [InlineData(FormDocumentFormat.WordMacroEnabled, StructuredAttachmentRejection.MacroEnabled)]
    [InlineData(FormDocumentFormat.WordLegacy, StructuredAttachmentRejection.Legacy)]
    public async Task A_word_with_macros_or_older_than_2007_is_rejected_without_reading_it(FormDocumentFormat format, StructuredAttachmentRejection rejection)
    {
        var spreadsheets = new Spreadsheets();
        var documents = new Documents { Format = format };

        var preparation = await Reader(spreadsheets, documents).PrepareAsync(Content, "contrato.xlsx", Ct);

        Assert.Equal((rejection, StructuredAttachmentFileKind.Word), (preparation.Rejection, preparation.FileKind));
        Assert.Null(documents.ReadFormat);
        Assert.False(spreadsheets.Detected);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader(new(), new()).PrepareAsync(Content, "p.csv", cancelled.Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_module_replaces_the_core_reader_once_in_either_registration_order(bool coreFirst)
    {
        var services = new ServiceCollection();
        if (coreFirst) services.AddApplication().AddBusinessFormsApplication();
        else services.AddBusinessFormsApplication().AddApplication();

        var registration = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IStructuredAttachmentReader));
        Assert.Equal((typeof(BusinessFormAttachmentReader), ServiceLifetime.Scoped), (registration.ImplementationType, registration.Lifetime));
    }

    private static string Sha(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static BusinessFormAttachmentReader Reader(Spreadsheets spreadsheets, Documents documents, FakePermissionService? permissions = null,
        FakeCurrentUser? current = null)
    {
        permissions ??= new FakePermissionService();
        current ??= new FakeCurrentUser();
        return new BusinessFormAttachmentReader(new FormCatalogAccessGuard(current, permissions), new FormDraftAccessGuard(current, permissions),
            spreadsheets, documents);
    }

    private sealed class Spreadsheets : IFormSpreadsheetReader
    {
        public const string Sha = "2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae";
        public FormSpreadsheetFormat Format { get; init; } = FormSpreadsheetFormat.Csv;
        public Error? Error { get; init; }
        public bool Detected { get; private set; }
        public FormSpreadsheetFormat? ReadFormat { get; private set; }
        public byte[]? ReadBytes { get; private set; }

        public FormSpreadsheetFormat DetectFormat(byte[] content)
        {
            Detected = true;
            return Format;
        }

        public Result<FormSpreadsheet> Read(byte[] content, FormSpreadsheetFormat format, CancellationToken cancellationToken)
        {
            (ReadBytes, ReadFormat) = (content, format);
            Result<FormSpreadsheet> result = Error is { } error ? error
                : new FormSpreadsheet("", Sha, [new(1, [new("CUIT"), new("Razón social")]), new(2, [new("30-71234567-8"), new("Agro Sur")])]);
            return result;
        }

        public Task<Result<FormSpreadsheet>> ReadAsync(Stream content, string fileName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    // The document reader of the module: what it says the bytes are and what it read of them.
    private sealed class Documents : IFormDocumentReader
    {
        public FormDocumentFormat Format { get; init; } = FormDocumentFormat.Unknown;
        public FormDocumentContent Content { get; init; } = new(FormDocumentReadStatus.Unreadable);
        public bool Detected { get; private set; }
        public FormDocumentFormat? ReadFormat { get; private set; }
        public byte[]? ReadBytes { get; private set; }

        public FormDocumentFormat DetectFormat(byte[] content)
        {
            Detected = true;
            return Format;
        }

        public Task<FormDocumentContent> ReadAsync(byte[] content, FormDocumentFormat format, CancellationToken cancellationToken)
        {
            (ReadBytes, ReadFormat) = (content, format);
            return Task.FromResult(Content);
        }
    }
}
