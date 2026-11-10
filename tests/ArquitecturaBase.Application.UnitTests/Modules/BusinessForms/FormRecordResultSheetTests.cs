using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// RE1-A in the sheet and the Excel: a column per datum of the results, after the questions, named by the latest result of its
// source; a response without a result has no values, and the Excel types each cell by its kind.
public sealed class FormRecordResultSheetTests
{
    private static FormRecordResultRow Result(Guid recordId, string voucher, string expiresOn) => new()
    {
        RecordId = recordId, RunId = Guid.NewGuid(), Source = "arca.invoice", RecordedAtUtc = new DateTime(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc),
        SourceNameJson = """{"es":"Emitir comprobante en ARCA"}""",
        ValuesJson = FormDraftPolicy.Serialize(new[]
        {
            new StructuredRecordResultValue("voucher", new Dictionary<string, string> { ["es"] = "Comprobante" }, "text", voucher),
            new StructuredRecordResultValue("expiresOn", new Dictionary<string, string> { ["es"] = "Vencimiento del código" }, "date", expiresOn),
        }),
    };

    private static (FakeFormResponseReader Reader, FormRecordResultReaderDouble Results, FakeCurrentUser User, FakePermissionService Permissions) Arrange()
    {
        var reader = new FakeFormResponseReader(); reader.ArrangeExport(2);
        var results = new FormRecordResultReaderDouble();
        results.Rows.Add(Result(reader.ExportRows[0].Record.RecordId, "Factura C 0004-00000012", "2026-11-19"));
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport];
        return (reader, results, user, permissions);
    }

    [Fact]
    public async Task The_sheet_has_a_column_per_datum_and_each_response_its_values()
    {
        var (reader, results, user, permissions) = Arrange();
        var service = new FormResponseService(reader, new(user, permissions, reader), RequestValidators.For(new ListFormRecordsRequestValidator()),
            NullLogger<FormResponseService>.Instance, new([]), new FormSuggestionFixture(),
            new(user, permissions, new ArquitecturaBase.Application.UnitTests.TestDoubles.Users.InMemoryUserAccounts()), results);

        var sheet = (await service.ListAsync(reader.Definition!.Id, new(), CancellationToken.None)).Value;

        Assert.Equal(["arca.invoice.voucher", "arca.invoice.expiresOn"], sheet.ResultColumns!.Select(column => column.Id));
        Assert.Equal(["Comprobante", "Vencimiento del código"], sheet.ResultColumns!.Select(column => column.Label));
        Assert.Equal("Emitir comprobante en ARCA", sheet.ResultColumns![0].SourceName);
        var first = sheet.Records.Items.Single(row => row.Id == reader.ExportRows[0].Record.RecordId);
        Assert.Equal("2026-11-19", first.Results!["arca.invoice.expiresOn"]);
        Assert.Null(sheet.Records.Items.Single(row => row.Id == reader.ExportRows[1].Record.RecordId).Results);
    }

    [Fact]
    public async Task Without_results_the_sheet_keeps_its_body()
    {
        var (reader, _, user, permissions) = Arrange();
        var service = new FormResponseService(reader, new(user, permissions, reader), RequestValidators.For(new ListFormRecordsRequestValidator()),
            NullLogger<FormResponseService>.Instance, new([]), new FormSuggestionFixture(),
            new(user, permissions, new ArquitecturaBase.Application.UnitTests.TestDoubles.Users.InMemoryUserAccounts()), new FormRecordResultReaderDouble());

        var sheet = (await service.ListAsync(reader.Definition!.Id, new(), CancellationToken.None)).Value;

        Assert.Null(sheet.ResultColumns);
        Assert.All(sheet.Records.Items, row => Assert.Null(row.Results));
        Assert.DoesNotContain("resultColumns", FormDraftPolicy.Serialize(sheet));
    }

    [Fact]
    public async Task The_excel_adds_the_data_after_the_questions_typed_by_their_kind()
    {
        var (reader, results, user, permissions) = Arrange();
        var writer = new FakeFormWorkbookWriter();
        var service = new FormExportService(reader, writer, new(user, permissions, reader),
            RequestValidators.For(new FormExportRequestValidator(), new ListFormRecordsRequestValidator()), new FakeTimeProvider(),
            NullLogger<FormExportService>.Instance, new(new([]), new ArquitecturaBase.Application.UnitTests.TestDoubles.Users.InMemoryUserAccounts()), results);

        Assert.True((await service.ExportAsync(reader.Definition!.Id, new(new(), null), CancellationToken.None)).IsSuccess);

        var sheet = writer.Request!.Sheets[0];
        Assert.Equal(["Comprobante", "Vencimiento del código"], sheet.Headers.TakeLast(2));
        var first = sheet.Rows[0];
        Assert.Equal(("text", (object?)"Factura C 0004-00000012"), (first[^2].Kind, first[^2].Value));
        Assert.Equal(("date", (object?)new DateTime(2026, 11, 19)), (first[^1].Kind, first[^1].Value));
        Assert.Null(sheet.Rows[1][^1].Value);
    }
}
