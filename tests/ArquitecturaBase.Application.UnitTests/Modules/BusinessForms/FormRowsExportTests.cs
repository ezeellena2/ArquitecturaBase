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

/// <summary>The Excel of a form with a block of rows: the amount of items in «Respuestas» and a sheet of its items (RG4-A).</summary>
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
    public async Task The_block_has_a_sheet_with_one_row_per_item_in_the_order_of_the_responses(string culture)
    {
        var ports = Ports();

        var result = await InCulture(culture, () => ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new(), null), Ct));

        Assert.True(result.IsSuccess);
        var sheets = ports.Writer.Request!.Sheets;
        Assert.Equal(culture == "es" ? ["Respuestas", "Artículos"] : ["Responses", "Articles"], sheets.Select(sheet => sheet.Name));
        Assert.Equal(culture == "es" ? ["Proveedor", "Artículos"] : ["Supplier", "Articles"], sheets[0].Headers.Skip(4));
        Assert.Equal([2L, null, 1L], sheets[0].Rows.Select(row => row[5].Value));
        Assert.Equal(culture == "es"
            ? ["Número", "Ítem", "Artículo", "Artículo · valor", "Artículo · Días", "Cantidad", "Observación (desde v2)"]
            : ["Number", "Item", "Article", "Article · value", "Article · Days", "Quantity", "Note (since v2)"], sheets[1].Headers);
        Assert.Equal(3, sheets[1].Rows.Count);
        Assert.Equal(["T-000001", "T-000001", "T-000003"], sheets[1].Rows.Select(row => row[0].Value));
        Assert.Equal([1L, 2L, 1L], sheets[1].Rows.Select(row => row[1].Value));
        Assert.All(sheets[1].Rows, row => Assert.Equal(["Semilla", 1.5m, 30L], row.Skip(2).Take(3).Select(cell => cell.Value)));
        Assert.Equal([200.5m, 100m, 7m], sheets[1].Rows.Select(row => row[5].Value));
        // The second item left its note empty; the block of the first version had no note.
        Assert.Equal(["Sin daños", null, "—"], sheets[1].Rows.Select(row => row[6].Value));
    }

    [Fact]
    public async Task A_hidden_block_has_no_sheet_and_its_questions_take_the_catalog_fields_chosen()
    {
        var ports = Ports();
        var id = ports.Reader.Definition!.Id;

        var hidden = await InCulture("es", () => ports.Service.ExportAsync(id, new(new(), [Supplier]), Ct));
        Assert.True(hidden.IsSuccess);
        Assert.Single(ports.Writer.Request!.Sheets);

        var withoutFields = await InCulture("es", () => ports.Service.ExportAsync(id, new(new(), [Group], []), Ct));
        Assert.True(withoutFields.IsSuccess);
        Assert.DoesNotContain("Artículo · Días", ports.Writer.Request!.Sheets[1].Headers);

        var chosen = await InCulture("es", () => ports.Service.ExportAsync(id, new(new(), null, [new(Item, Days)]), Ct));
        Assert.True(chosen.IsSuccess);
        Assert.Contains("Artículo · Días", ports.Writer.Request!.Sheets[1].Headers);

        var unknown = await InCulture("es", () => ports.Service.ExportAsync(id, new(new(), null, [new(Item, Guid.NewGuid())]), Ct));
        Assert.Equal(FormRecordErrors.FilterInvalid, unknown.Error);
    }

    private static async Task<T> InCulture<T>(string culture, Func<Task<T>> action)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try { return await action(); }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    /// <summary>Three responses: two items and none in the active version, and one item in the first version, whose block had no note.</summary>
    private static (FormExportService Service, FakeFormWorkbookWriter Writer, FakeFormResponseReader Reader) Ports()
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
        var old = initial.Versions[0] with { Id = Guid.NewGuid(), Number = 1, SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [supplier, group, item, quantity])) };
        var active = initial.Versions[0] with { Id = Guid.NewGuid(), Number = 2, SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [supplier, group, item, quantity, note])) };
        reader.Definition = new(initial.Id, initial.Prefix, active.Id, [old, active],
            CatalogValueTypes: new Dictionary<Guid, string> { [Catalog] = "Decimal" },
            CatalogFields: new Dictionary<Guid, IReadOnlyList<FormResponseCatalogFieldRow>> { [Catalog] = [new(Days, "days", Text("Días", "Days"), "integer", "data")] });
        var fields = FormDraftPolicy.Serialize(new[] { new FormCatalogCapturedField(Days, "days", Text("Días", "Days"), "integer", "data", JsonSerializer.SerializeToElement(30)) });
        object[] Answer(decimal amount, string? text = null) => text is null
            ? [new { fieldId = Item, value = Seed }, new { fieldId = Quantity, value = amount }]
            : [new { fieldId = Item, value = Seed }, new { fieldId = Quantity, value = amount }, new { fieldId = Note, value = text }];
        var answers = new[] { new[] { Answer(200.5m, "Sin daños"), Answer(100m) }, [], new[] { Answer(7m) } };
        reader.ExportRows = reader.ExportRows.Select((row, index) =>
        {
            var version = index == 2 ? old : active;
            return row with { Version = version, Record = row.Record with
            {
                VersionId = version.Id, VersionNumber = version.Number, SchemaJson = version.SchemaJson,
                ValuesJson = JsonSerializer.Serialize(new object[] { new { fieldId = Supplier, value = "Proveedor del norte" }, new { fieldId = Group, value = answers[index] } }),
                References = answers[index].Select((_, position) => new FormRecordReferenceRow(row.Record.RecordId, Item, Catalog, 1, Seed, "Semilla", null, "Insumos",
                    Value: "1.5", ValueType: "Decimal", FieldValuesJson: fields, Row: position)).ToArray(),
            } };
        }).ToArray();
        var writer = new FakeFormWorkbookWriter();
        var service = new FormExportService(reader, writer, new(actor, permissions, reader),
            RequestValidators.For(new FormExportRequestValidator(), new ListFormRecordsRequestValidator()), new FakeTimeProvider(), NullLogger<FormExportService>.Instance, new([]));
        return (service, writer, reader);
    }
    private static CanonicalFormField Field(Guid id, string type, Dictionary<string, string> label) =>
        new(id, "field_" + id.ToString("N"), type, label, null, true, type == "longText" ? 600 : type == "shortText" ? 80 : null, null);
    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
}
