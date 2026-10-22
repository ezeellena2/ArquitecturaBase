using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogExportServiceTests
{
    [Theory]
    [InlineData("es", "Integer", "123456789012345678")]
    [InlineData("en", "Integer", "123456789012345678")]
    [InlineData("es", "Decimal", "0.1234567890123456789012345678")]
    [InlineData("en", "Decimal", "0.1234567890123456789012345678")]
    [InlineData("es", "Decimal", "0.001")]
    [InlineData("es", "Decimal", "12.345")]
    [InlineData("es", "Date", "2026-10-08")]
    [InlineData("en", "Date", "2026-10-08")]
    public async Task Export_includes_inactive_rows_and_canonical_values_as_literal_text(string culture, string type, string value)
    {
        using var scope = new CultureScope(culture);
        var fixture = Create();
        fixture.Reader.Catalog = fixture.Reader.Catalog! with { ValueType = type };
        var id = fixture.Reader.Catalog.Id;
        var secondValue = type == "Date" ? "2026-10-09" : "0";
        fixture.Reader.Items = [new(Guid.NewGuid(), id, "=SUM(A1)", "=2+2", "English", "Detalle", "Detail", 1, true, value),
            new(Guid.NewGuid(), id, "0002", "Inactiva", null, null, null, 2, false, secondValue)];

        var result = await fixture.Service.ExportAsync(id, false, Ct);

        Assert.True(result.IsSuccess);
        var workbook = fixture.Writer.Request!;
        Assert.Equal(7, workbook.Headers.Count);
        Assert.Equal(culture == "es" ? "Código" : "Code", workbook.Headers[0]);
        var valueType = culture == "es" ? type switch { "Integer" => "entero", "Date" => "fecha", _ => "decimal" } : type.ToLowerInvariant();
        Assert.Equal((culture == "es" ? "Valor canónico (" : "Canonical value (") + valueType + ")", workbook.Headers[6]);
        Assert.Equal(2, workbook.Rows.Count);
        Assert.All(workbook.Rows.SelectMany(row => row), cell => Assert.Equal("text", cell.Kind));
        Assert.Equal(["=SUM(A1)", "=2+2", "English", "Detalle", "Detail", culture == "es" ? "Sí" : "Yes", value], workbook.Rows[0].Select(cell => cell.Value));
        Assert.Equal("No", workbook.Rows[1][5].Value);
        Assert.Equal(secondValue, workbook.Rows[1][6].Value);
        Assert.Equal(1, fixture.Reader.DefinitionReads);
        Assert.Equal(1, fixture.Reader.AllItemsReads);
        Assert.EndsWith(".xlsx", result.Value.FileName, StringComparison.Ordinal);

        var sheet = new FormSpreadsheet("Options", new('c', 64),
            new[] { new FormSpreadsheetRow(1, workbook.Headers.Select(header => new FormSpreadsheetCell(header)).ToArray()) }
                .Concat(workbook.Rows.Select((row, index) => new FormSpreadsheetRow(index + 2,
                    row.Select(cell => new FormSpreadsheetCell((string?)cell.Value)).ToArray()))).ToArray());
        var imported = FormCatalogImportPolicy.Build(new(id, 1, Enum.Parse<BusinessFormCatalogValueType>(type), fixture.Reader.Items),
            sheet, FormCatalogImportMode.Replace, culture);
        Assert.True(imported.IsSuccess);
        Assert.Empty(imported.Value.Preview.Issues);
        Assert.Equal(2, imported.Value.Preview.Counts.Unchanged);
        Assert.Empty(imported.Value.Items);
    }

    [Theory]
    [InlineData("es", BusinessFormCatalogValueType.Text, 6)]
    [InlineData("en", BusinessFormCatalogValueType.Text, 6)]
    [InlineData("es", BusinessFormCatalogValueType.Integer, 7)]
    [InlineData("en", BusinessFormCatalogValueType.Integer, 7)]
    [InlineData("es", BusinessFormCatalogValueType.Decimal, 7)]
    [InlineData("en", BusinessFormCatalogValueType.Date, 7)]
    public async Task Template_has_only_the_localized_rules_row_without_fictitious_options(string culture, BusinessFormCatalogValueType type, int count)
    {
        using var scope = new CultureScope(culture);
        var fixture = Create(); fixture.Reader.Catalog = fixture.Reader.Catalog! with { ValueType = type.ToString() };

        var result = await fixture.Service.ExportAsync(fixture.Reader.Catalog.Id, true, Ct);

        Assert.True(result.IsSuccess);
        var columns = FormCatalogImportTemplate.Columns(type, culture);
        Assert.Equal(count, columns.Count);
        Assert.Equal(new[] { "code", "text", "textEn", "detail", "detailEn", "active" }, columns.Take(6).Select(column => column.Key));
        Assert.Equal(columns.Select(column => column.Header), fixture.Writer.Request!.Headers);
        var row = Assert.Single(fixture.Writer.Request.Rows);
        Assert.Equal(columns.Select(column => column.Rule), row.Select(cell => cell.Value));
        Assert.Equal("", row[0].Value);
        Assert.All(columns.Skip(1), column => Assert.False(string.IsNullOrWhiteSpace(column.Rule)));
        Assert.Equal(0, fixture.Reader.AllItemsReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_read_permission_never_reads_or_writes_a_workbook(bool authenticated)
    {
        var fixture = Create();
        if (!authenticated) fixture.Current.UserId = null;
        fixture.Permissions.Permissions.Clear();
        var result = await fixture.Service.ExportAsync(Guid.NewGuid(), false, Ct);
        Assert.Equal(authenticated ? FormDefinitionErrors.Forbidden : FormDefinitionErrors.Unauthorized, result.Error);
        Assert.Equal(0, fixture.Reader.DefinitionReads);
        Assert.Null(fixture.Writer.Request);
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("items")]
    [InlineData("writer")]
    public async Task Revocation_discards_the_export_at_each_read_boundary(string boundary)
    {
        var fixture = Create();
        void Revoke() => fixture.Permissions.Permissions.Clear();
        if (boundary == "definition") fixture.Reader.AfterDefinition = Revoke;
        else if (boundary == "items") fixture.Reader.AfterItems = Revoke;
        else fixture.Writer.BeforeReturn = Revoke;
        var result = await fixture.Service.ExportAsync(fixture.Reader.Catalog!.Id, false, Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        if (boundary == "definition") Assert.Equal(0, fixture.Reader.AllItemsReads);
        if (boundary != "writer") Assert.Null(fixture.Writer.Request);
    }

    [Fact]
    public async Task Filters_do_not_read_manual_items_or_emit_a_template()
    {
        var fixture = Create(); fixture.Reader.Catalog = fixture.Reader.Catalog! with { Kind = "Filters" };
        var result = await fixture.Service.ExportAsync(fixture.Reader.Catalog.Id, true, Ct);
        Assert.Equal(FormCatalogErrors.ManualItemsUnavailable, result.Error);
        Assert.Equal(0, fixture.Reader.AllItemsReads); Assert.Null(fixture.Writer.Request);
    }

    [Fact]
    public async Task A_missing_catalog_does_not_emit_a_workbook()
    {
        var fixture = Create(); fixture.Reader.Catalog = null;
        var result = await fixture.Service.ExportAsync(Guid.NewGuid(), false, Ct);
        Assert.Equal(FormCatalogErrors.NotFound, result.Error); Assert.Null(fixture.Writer.Request);
    }

    private static (FormCatalogExportService Service, FormCatalogExportReader Reader, FakeFormWorkbookWriter Writer,
        FakePermissionService Permissions, FakeCurrentUser Current) Create()
    {
        var current = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService(); permissions.Permissions[current.UserId.Value] = [BusinessFormsPermissions.CatalogsRead];
        var reader = new FormCatalogExportReader(); var writer = new FakeFormWorkbookWriter();
        return (new(reader, writer, new(current, permissions)), reader, writer, permissions, current);
    }
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentUICulture;
        public CultureScope(string culture) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        public void Dispose() => CultureInfo.CurrentUICulture = previous;
    }
}
