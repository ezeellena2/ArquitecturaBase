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

    // «Solo las visibles» (2026-11-11): the request names the visible data of the results by their ids, and only those go. An id
    // that is no longer among the latest results is left out instead of failing: a new result can change the data of its source
    // after the sheet was read.
    [Fact]
    public async Task The_excel_of_the_visible_columns_takes_only_the_result_data_it_names()
    {
        var (reader, results, user, permissions) = Arrange();
        var writer = new FakeFormWorkbookWriter();

        Assert.True(await ExportedAsync(reader, results, user, permissions, writer, ["arca.invoice.expiresOn", "arca.invoice.gone"]));

        var sheet = writer.Request!.Sheets[0];
        Assert.Equal("Vencimiento del código", sheet.Headers[^1]);
        Assert.DoesNotContain("Comprobante", sheet.Headers);
        Assert.Equal(("date", (object?)new DateTime(2026, 11, 19)), (sheet.Rows[0][^1].Kind, sheet.Rows[0][^1].Value));
        Assert.All(sheet.Rows, row => Assert.Equal(sheet.Headers.Count, row.Count));
    }

    [Fact]
    public async Task The_excel_of_the_visible_columns_without_result_data_leaves_the_results_out()
    {
        var (reader, results, user, permissions) = Arrange();
        var writer = new FakeFormWorkbookWriter();

        Assert.True(await ExportedAsync(reader, results, user, permissions, writer, []));

        var sheet = writer.Request!.Sheets[0];
        Assert.DoesNotContain("Comprobante", sheet.Headers);
        Assert.DoesNotContain("Vencimiento del código", sheet.Headers);
        Assert.All(sheet.Rows, row => Assert.Equal(sheet.Headers.Count, row.Count));
    }

    [Fact]
    public void The_visible_result_data_are_named_once_each()
    {
        var validator = new FormExportRequestValidator();

        Assert.False(validator.Validate(new FormExportRequest(new(), null, ResultColumns: ["arca.invoice.voucher", "arca.invoice.voucher"])).IsValid);
        Assert.False(validator.Validate(new FormExportRequest(new(), null, ResultColumns: [null])).IsValid);
        Assert.False(validator.Validate(new FormExportRequest(new(), null, ResultColumns: [" "])).IsValid);
        Assert.True(validator.Validate(new FormExportRequest(new(), null, ResultColumns: [])).IsValid);
        Assert.True(validator.Validate(new FormExportRequest(new(), null, ResultColumns: ["arca.invoice.voucher"])).IsValid);
    }

    private static async Task<bool> ExportedAsync(FakeFormResponseReader reader, FormRecordResultReaderDouble results, FakeCurrentUser user,
        FakePermissionService permissions, FakeFormWorkbookWriter writer, IReadOnlyList<string?> resultColumns)
    {
        var service = new FormExportService(reader, writer, new(user, permissions, reader),
            RequestValidators.For(new FormExportRequestValidator(), new ListFormRecordsRequestValidator()), new FakeTimeProvider(),
            NullLogger<FormExportService>.Instance, new(new([]), new ArquitecturaBase.Application.UnitTests.TestDoubles.Users.InMemoryUserAccounts()), results);
        return (await service.ExportAsync(reader.Definition!.Id, new(new(), null, ResultColumns: resultColumns), CancellationToken.None)).IsSuccess;
    }
}
