using System.Text;
using ArquitecturaBase.Application.Interfaces.Channels;
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

// Stage 2 of the assistant authoring: Home and WhatsApp read a spreadsheet through the neutral port, without naming the module.
public sealed class BusinessFormAttachmentReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly byte[] Content = Encoding.UTF8.GetBytes("CUIT;Razón social\n30-71234567-8;Agro Sur\n");

    [Fact]
    public async Task Only_who_reads_and_manages_catalogs_attaches_and_it_is_the_named_account_not_the_current_user()
    {
        Guid actor = Guid.NewGuid(), other = Guid.NewGuid();
        var permissions = new FakePermissionService();
        var current = new FakeCurrentUser { UserId = other };
        permissions.Permissions[other] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        var reader = new BusinessFormAttachmentReader(new FormCatalogAccessGuard(current, permissions), new Spreadsheets());

        Assert.False(await reader.CanAttachAsync(actor, Ct));
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead];
        Assert.False(await reader.CanAttachAsync(actor, Ct));
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsManage];
        Assert.False(await reader.CanAttachAsync(actor, Ct));
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        Assert.True(await reader.CanAttachAsync(actor, Ct));
        current.UserId = null;
        Assert.True(await reader.CanAttachAsync(actor, Ct));
    }

    [Theory]
    [InlineData(FormSpreadsheetFormat.Csv, "proveedores.xlsx", "proveedores.xlsx", "proveedores.csv", StructuredSpreadsheetKind.Csv, "text/csv")]
    [InlineData(FormSpreadsheetFormat.Xlsx, "C:\\fakepath\\proveedores.csv", "proveedores.csv", "proveedores.xlsx", StructuredSpreadsheetKind.Xlsx,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(FormSpreadsheetFormat.Csv, "carpeta/proveedores", "proveedores", "proveedores.csv", StructuredSpreadsheetKind.Csv, "text/csv")]
    public async Task The_kind_comes_from_the_bytes_and_the_reader_gets_its_extension(FormSpreadsheetFormat format, string name, string shown,
        string read, StructuredSpreadsheetKind kind, string mediaType)
    {
        var spreadsheets = new Spreadsheets { Format = format };

        var preparation = await Reader(spreadsheets).PrepareAsync(Content, name, Ct);

        Assert.Equal(read, spreadsheets.ReadName);
        Assert.Equal(Content, spreadsheets.ReadBytes);
        var prepared = preparation.Spreadsheet!;
        Assert.Equal((shown, kind, mediaType, (long)Content.Length, Spreadsheets.Sha, "proveedores", 2),
            (prepared.FileName, prepared.Kind, prepared.MediaType, prepared.Size, prepared.Sha256, prepared.SheetName, prepared.RowCount));
    }

    [Theory]
    [InlineData(FormSpreadsheetFormat.MacroEnabled, StructuredAttachmentRejection.MacroEnabled)]
    [InlineData(FormSpreadsheetFormat.LegacyOrProtected, StructuredAttachmentRejection.LegacyOrProtected)]
    [InlineData(FormSpreadsheetFormat.Unknown, StructuredAttachmentRejection.Unreadable)]
    public async Task What_the_bytes_are_rejects_before_reading_them(FormSpreadsheetFormat format, StructuredAttachmentRejection rejection)
    {
        var spreadsheets = new Spreadsheets { Format = format };

        var preparation = await Reader(spreadsheets).PrepareAsync(Content, "proveedores.xlsx", Ct);

        Assert.Equal(rejection, preparation.Rejection);
        Assert.Null(preparation.Spreadsheet);
        Assert.Null(spreadsheets.ReadName);
    }

    [Fact]
    public async Task The_limits_of_the_import_are_too_large_and_any_other_failure_is_unreadable()
    {
        Error[] limits = [FormCatalogImportErrors.FileTooLarge, FormCatalogImportErrors.TooManyRows, FormCatalogImportErrors.TooManyColumns,
            FormCatalogImportErrors.InSheet(FormCatalogImportErrors.TooManyCells, "Hoja2")];
        foreach (var error in limits)
            Assert.Equal(StructuredAttachmentRejection.TooLarge, (await Reader(new() { Error = error }).PrepareAsync(Content, "p.csv", Ct)).Rejection);
        Assert.Equal(StructuredAttachmentRejection.Unreadable,
            (await Reader(new() { Error = FormCatalogImportErrors.FileUnreadable }).PrepareAsync(Content, "p.csv", Ct)).Rejection);

        var spreadsheets = new Spreadsheets();
        var oversized = await Reader(spreadsheets).PrepareAsync(new byte[FormCatalogImportLimits.MaximumFileBytes + 1], "p.csv", Ct);
        Assert.Equal(StructuredAttachmentRejection.TooLarge, oversized.Rejection);
        Assert.False(spreadsheets.Detected);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader(new()).PrepareAsync(Content, "p.csv", cancelled.Token));
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

    private static BusinessFormAttachmentReader Reader(Spreadsheets spreadsheets)
    {
        var permissions = new FakePermissionService();
        return new BusinessFormAttachmentReader(new FormCatalogAccessGuard(new FakeCurrentUser(), permissions), spreadsheets);
    }

    private sealed class Spreadsheets : IFormSpreadsheetReader
    {
        public const string Sha = "2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae";
        public FormSpreadsheetFormat Format { get; init; } = FormSpreadsheetFormat.Csv;
        public Error? Error { get; init; }
        public bool Detected { get; private set; }
        public string? ReadName { get; private set; }
        public byte[]? ReadBytes { get; private set; }

        public FormSpreadsheetFormat DetectFormat(byte[] content)
        {
            Detected = true;
            return Format;
        }

        public async Task<Result<FormSpreadsheet>> ReadAsync(Stream content, string fileName, CancellationToken cancellationToken)
        {
            ReadName = fileName;
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            ReadBytes = copy.ToArray();
            Result<FormSpreadsheet> result = Error is { } error ? error
                : new FormSpreadsheet("proveedores", Sha, [new(1, [new("CUIT"), new("Razón social")]), new(2, [new("30-71234567-8"), new("Agro Sur")])]);
            return result;
        }
    }
}
