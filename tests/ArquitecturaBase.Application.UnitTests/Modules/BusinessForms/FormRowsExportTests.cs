using System.Globalization;
using System.Text.Json;
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

/// <summary>The Excel of a form with a block of rows: one sheet with a row per item, the data of its response repeated (XL1-A).</summary>
public sealed class FormRowsExportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid Supplier = Guid.NewGuid();
    private static readonly Guid Group = Guid.NewGuid();
    private static readonly Guid Item = Guid.NewGuid();
    private static readonly Guid Quantity = Guid.NewGuid();
    private static readonly Guid Note = Guid.NewGuid();
    private static readonly Guid Catalog = Guid.NewGuid();
    private static readonly Guid Seed = Guid.NewGuid();
    private static readonly Guid Days = Guid.NewGuid();

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task The_block_goes_in_the_same_sheet_with_one_row_per_item_in_the_order_of_the_responses(string culture)
    {
        var ports = Ports();

        var result = await InCulture(culture, () => ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new(), null), Ct));

        Assert.True(result.IsSuccess);
        var sheet = Assert.Single(ports.Writer.Request!.Sheets);
        Assert.Equal(culture == "es" ? "Respuestas" : "Responses", sheet.Name);
        // «Ítem» and the questions of the block take its place.
        Assert.Equal(culture == "es"
            ? ["Proveedor", "Ítem", "Artículo", "Artículo · valor", "Artículo · Días", "Cantidad", "Observación (desde v2)"]
            : ["Supplier", "Item", "Article", "Article · value", "Article · Days", "Quantity", "Note (since v2)"], sheet.Headers.Skip(5));
        // Two items, a response of the active version without items and an item of the first version.
        Assert.Equal(["T-000001", "T-000001", "T-000002", "T-000003"], sheet.Rows.Select(row => row[0].Value));
        Assert.All(sheet.Rows, row => Assert.Equal("Proveedor del norte", row[5].Value));
        Assert.Equal([1L, 2L, null, 1L], sheet.Rows.Select(row => row[6].Value));
        Assert.Equal(["Semilla", "Semilla", null, "Semilla"], sheet.Rows.Select(row => row[7].Value));
        Assert.Equal([1.5m, 1.5m, null, 1.5m], sheet.Rows.Select(row => row[8].Value));
        Assert.Equal([30L, 30L, null, 30L], sheet.Rows.Select(row => row[9].Value));
        Assert.Equal([200.5m, 100m, null, 7m], sheet.Rows.Select(row => row[10].Value));
        // The second item left its note empty; the block of the first version had no note.
        Assert.Equal(["Sin daños", null, null, "—"], sheet.Rows.Select(row => row[11].Value));
        // Each response starts a group, so the writer can mark where it starts.
        Assert.Equal([0, 2, 3], sheet.GroupStarts);
    }

    [Fact]
    public async Task A_hidden_block_has_no_item_columns_and_its_questions_take_the_catalog_fields_chosen()
    {
        var ports = Ports();
        var id = ports.Reader.Definition!.Id;

        var hidden = await InCulture("es", () => ports.Service.ExportAsync(id, new(new(), [Supplier]), Ct));
        Assert.True(hidden.IsSuccess);
        var withoutBlock = Assert.Single(ports.Writer.Request!.Sheets);
        Assert.Equal(["Proveedor"], withoutBlock.Headers.Skip(5));
        Assert.Equal(3, withoutBlock.Rows.Count);
        Assert.Null(withoutBlock.GroupStarts);

        var withoutFields = await InCulture("es", () => ports.Service.ExportAsync(id, new(new(), [Group], []), Ct));
        Assert.True(withoutFields.IsSuccess);
        Assert.Equal(["Ítem", "Artículo", "Artículo · valor", "Cantidad", "Observación (desde v2)"], Assert.Single(ports.Writer.Request!.Sheets).Headers.Skip(5));

        var chosen = await InCulture("es", () => ports.Service.ExportAsync(id, new(new(), null, [new(Item, Days)]), Ct));
        Assert.True(chosen.IsSuccess);
        Assert.Contains("Artículo · Días", Assert.Single(ports.Writer.Request!.Sheets).Headers);

        var unknown = await InCulture("es", () => ports.Service.ExportAsync(id, new(new(), null, [new(Item, Guid.NewGuid())]), Ct));
        Assert.Equal(FormRecordErrors.FilterInvalid, unknown.Error);
    }

    [Fact]
    public async Task A_response_of_a_version_without_the_block_marks_the_item_and_its_questions_as_missing()
    {
        var ports = Ports(blockInFirstVersion: false);

        var result = await InCulture("es", () => ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new(), null), Ct));

        Assert.True(result.IsSuccess);
        var sheet = Assert.Single(ports.Writer.Request!.Sheets);
        Assert.Equal("T-000003", sheet.Rows[^1][0].Value);
        Assert.Equal(["—", "—", "—", "—", "—", "—"], sheet.Rows[^1].Skip(6).Select(cell => cell.Value));
    }

    [Fact]
    public async Task A_voided_response_says_so_in_each_of_its_items()
    {
        var ports = Ports();
        ports.Reader.ExportRows = ports.Reader.ExportRows.Select((row, index) => index == 0 ? row with { State = nameof(FormRecordState.Voided) } : row).ToArray();

        var result = await InCulture("es", () => ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new() { IncludeVoided = true }, null), Ct));

        Assert.True(result.IsSuccess);
        var sheet = Assert.Single(ports.Writer.Request!.Sheets);
        Assert.Equal(["Anulada", "Proveedor", "Ítem"], sheet.Headers.Skip(5).Take(3));
        Assert.Equal(["Sí", "Sí", "", ""], sheet.Rows.Select(row => row[5].Value));
    }

    [Theory]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public async Task The_excel_goes_up_to_a_hundred_thousand_rows_and_stops_before_writing_more(int responses, bool exported)
    {
        var ports = Large(responses, BusinessFormRecordValue.MaxRows);

        var result = await ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new(), null), Ct);

        if (exported) Assert.Equal(FormExportService.MaximumRows, Assert.Single(ports.Writer.Request!.Sheets).Rows.Count);
        else
        {
            Assert.Equal(FormRecordErrors.ExportTooManyRows, result.Error);
            Assert.Null(ports.Writer.Request);
        }
    }

    private static async Task<T> InCulture<T>(string culture, Func<Task<T>> action)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try { return await action(); }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    /// <summary>
    /// Three responses: two items and none in the active version, and one in the first version, whose block had no note.
    /// Without the block in the first version, its response only answers the supplier.
    /// </summary>
    private static (FormExportService Service, FakeFormWorkbookWriter Writer, FakeFormResponseReader Reader) Ports(bool blockInFirstVersion = true)
    {
        var actor = new FakeCurrentUser { UserId = Guid.NewGuid() }; var permissions = new FakePermissionService();
        permissions.Permissions[actor.UserId.Value] = [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport];
        var reader = new FakeFormResponseReader(); reader.ArrangeExport(3);
        var supplier = Field(Supplier, "shortText", Text("Proveedor", "Supplier"));
        var group = Field(Group, "group", Text("Artículos", "Articles")) with { Required = false, Rows = new(0, 3) };
        var item = Field(Item, "choice", Text("Artículo", "Article")) with { Source = new(Catalog), Repeated = true };
        var quantity = Field(Quantity, "decimal", Text("Cantidad", "Quantity")) with { Repeated = true };
        var note = Field(Note, "longText", Text("Observación", "Note")) with { Required = false, Repeated = true };
        var initial = reader.Definition!;
        CanonicalFormField[] first = blockInFirstVersion ? [supplier, group, item, quantity] : [supplier];
        var old = initial.Versions[0] with { Id = Guid.NewGuid(), Number = 1, SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, first)) };
        var active = initial.Versions[0] with { Id = Guid.NewGuid(), Number = 2, SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [supplier, group, item, quantity, note])) };
        reader.Definition = new(initial.Id, initial.Prefix, active.Id, [old, active],
            CatalogValueTypes: new Dictionary<Guid, string> { [Catalog] = "Decimal" },
            CatalogFields: new Dictionary<Guid, IReadOnlyList<FormResponseCatalogFieldRow>> { [Catalog] = [new(Days, "days", Text("Días", "Days"), "integer", "data")] });
        var fields = FormDraftPolicy.Serialize(new[] { new FormCatalogCapturedField(Days, "days", Text("Días", "Days"), "integer", "data", JsonSerializer.SerializeToElement(30)) });
        object[] Answer(decimal amount, string? text = null) => text is null
            ? [new { fieldId = Item, value = Seed }, new { fieldId = Quantity, value = amount }]
            : [new { fieldId = Item, value = Seed }, new { fieldId = Quantity, value = amount }, new { fieldId = Note, value = text }];
        var answers = new[] { new[] { Answer(200.5m, "Sin daños"), Answer(100m) }, [], blockInFirstVersion ? new[] { Answer(7m) } : [] };
        reader.ExportRows = reader.ExportRows.Select((row, index) =>
        {
            var version = index == 2 ? old : active;
            var values = index == 2 && !blockInFirstVersion
                ? new object[] { new { fieldId = Supplier, value = "Proveedor del norte" } }
                : new object[] { new { fieldId = Supplier, value = "Proveedor del norte" }, new { fieldId = Group, value = answers[index] } };
            return row with { Version = version, Record = row.Record with
            {
                VersionId = version.Id, VersionNumber = version.Number, SchemaJson = version.SchemaJson,
                ValuesJson = JsonSerializer.Serialize(values),
                References = answers[index].Select((_, position) => new FormRecordReferenceRow(row.Record.RecordId, Item, Catalog, 1, Seed, "Semilla", null, "Insumos",
                    Value: "1.5", ValueType: "Decimal", FieldValuesJson: fields, Row: position)).ToArray(),
            } };
        }).ToArray();
        var writer = new FakeFormWorkbookWriter();
        var service = new FormExportService(reader, writer, new(actor, permissions, reader),
            RequestValidators.For(new FormExportRequestValidator(), new ListFormRecordsRequestValidator()), new FakeTimeProvider(), NullLogger<FormExportService>.Instance,
            new(new([]), new ArquitecturaBase.Application.UnitTests.TestDoubles.Users.InMemoryUserAccounts()), new FormRecordResultReaderDouble());
        return (service, writer, reader);
    }

    /// <summary>Responses whose block has the same quantity of items each.</summary>
    private static (FormExportService Service, FakeFormWorkbookWriter Writer, FakeFormResponseReader Reader) Large(int responses, int items)
    {
        var actor = new FakeCurrentUser { UserId = Guid.NewGuid() }; var permissions = new FakePermissionService();
        permissions.Permissions[actor.UserId.Value] = [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport];
        var reader = new FakeFormResponseReader(); reader.ArrangeExport(responses);
        var group = Field(Group, "group", Text("Artículos", "Articles")) with { Required = false, Rows = new(0, BusinessFormRecordValue.MaxRows) };
        var quantity = Field(Quantity, "decimal", Text("Cantidad", "Quantity")) with { Repeated = true };
        var initial = reader.Definition!;
        var version = initial.Versions[0] with { SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [group, quantity])) };
        reader.Definition = new(initial.Id, initial.Prefix, version.Id, [version]);
        var values = JsonSerializer.Serialize(new object[] { new { fieldId = Group,
            value = Enumerable.Range(1, items).Select(item => new object[] { new { fieldId = Quantity, value = (decimal)item } }).ToArray() } });
        reader.ExportRows = reader.ExportRows.Select(row => row with { Version = version,
            Record = row.Record with { SchemaJson = version.SchemaJson, ValuesJson = values } }).ToArray();
        var writer = new FakeFormWorkbookWriter();
        var service = new FormExportService(reader, writer, new(actor, permissions, reader),
            RequestValidators.For(new FormExportRequestValidator(), new ListFormRecordsRequestValidator()), new FakeTimeProvider(), NullLogger<FormExportService>.Instance,
            new(new([]), new ArquitecturaBase.Application.UnitTests.TestDoubles.Users.InMemoryUserAccounts()), new FormRecordResultReaderDouble());
        return (service, writer, reader);
    }
    private static CanonicalFormField Field(Guid id, string type, Dictionary<string, string> label) =>
        new(id, "field_" + id.ToString("N"), type, label, null, true, type == "longText" ? 600 : type == "shortText" ? 80 : null, null);
    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
}
