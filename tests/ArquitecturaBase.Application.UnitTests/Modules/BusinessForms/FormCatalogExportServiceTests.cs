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
    [InlineData("es", "Integer", "1500", "integer", "#,##0")]
    [InlineData("en", "Integer", "-1500", "integer", "#,##0")]
    [InlineData("es", "Decimal", "1234.5", "decimal", "#,##0.0")]
    [InlineData("en", "Decimal", "0.001", "decimal", "#,##0.000")]
    [InlineData("es", "Decimal", "-0.25", "decimal", "#,##0.00")]
    [InlineData("es", "Decimal", "1500", "decimal", "#,##0")]
    [InlineData("es", "Date", "2026-10-08", "date", "dd/mm/yyyy")]
    [InlineData("en", "Date", "2026-10-08", "date", "mm/dd/yyyy")]
    public async Task Export_includes_inactive_rows_and_writes_values_as_Excel_numbers_and_dates_in_the_menu_format(string culture,
        string type, string value, string kind, string format)
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
        var workbook = fixture.Writer.Request!.Sheets[0];
        // The catalog is Spanish: with the menu in Spanish the English columns stay out, and reimporting keeps them.
        var spanish = culture == "es";
        Assert.Equal(spanish ? 5 : 7, workbook.Headers.Count);
        Assert.Equal(spanish ? "Código" : "Code", workbook.Headers[0]);
        var valueType = spanish ? type switch { "Integer" => "entero", "Date" => "fecha", _ => "decimal" } : type.ToLowerInvariant();
        Assert.Equal((spanish ? "Valor (" : "Value (") + valueType + ")", workbook.Headers[^1]);
        Assert.Equal(2, workbook.Rows.Count);
        Assert.All(workbook.Rows.SelectMany(row => row.SkipLast(1)), cell => Assert.Equal("text", cell.Kind));
        Assert.Equal(spanish ? new object?[] { "=SUM(A1)", "=2+2", "Detalle", "Sí" }
            : new object?[] { "=SUM(A1)", "=2+2", "English", "Detalle", "Detail", "Yes" }, workbook.Rows[0].SkipLast(1).Select(cell => cell.Value));
        var cell = workbook.Rows[0][^1];
        Assert.Equal(kind, cell.Kind);
        Assert.Equal(format, cell.Format);
        Assert.Equal(Typed(type, value), cell.Value);
        Assert.Equal("No", workbook.Rows[1][^2].Value);
        Assert.Equal(Typed(type, secondValue), workbook.Rows[1][^1].Value);
        Assert.Equal(1, fixture.Reader.DefinitionReads);
        Assert.Equal(1, fixture.Reader.AllItemsReads);
        Assert.EndsWith(".xlsx", result.Value.FileName, StringComparison.Ordinal);

        var imported = FormCatalogImportPolicy.Build(new(id, 1, Enum.Parse<BusinessFormCatalogValueType>(type), "es", fixture.Reader.Items),
            FormCatalogExportSheet.Read(workbook), FormCatalogImportMode.Replace, culture);
        Assert.True(imported.IsSuccess);
        Assert.Empty(imported.Value.Preview.Issues);
        Assert.Equal(2, imported.Value.Preview.Counts.Unchanged);
        Assert.Empty(imported.Value.Items);
    }

    // An Excel number keeps 15 significant digits: a longer value goes as its exact text, which reimports unambiguously in both menus.
    [Theory]
    [InlineData("es", "Integer", "123456789012345678")]
    [InlineData("en", "Integer", "-1234567890123456")]
    [InlineData("es", "Decimal", "0.1234567890123456789012345678")]
    [InlineData("en", "Decimal", "0.1234567890123456789012345678")]
    [InlineData("es", "Decimal", "1234567.123456789012345")]
    [InlineData("es", "Decimal", "123.4567890123456")]
    public async Task A_value_longer_than_an_Excel_number_is_exported_as_its_exact_text(string culture, string type, string value)
    {
        using var scope = new CultureScope(culture);
        var fixture = Create();
        fixture.Reader.Catalog = fixture.Reader.Catalog! with { ValueType = type };
        var id = fixture.Reader.Catalog.Id;
        fixture.Reader.Items = [new(Guid.NewGuid(), id, "A", "Larga", null, null, null, 1, true, value),
            new(Guid.NewGuid(), id, "B", "Corta", null, null, null, 2, true, "123456789012345")];

        var result = await fixture.Service.ExportAsync(id, false, Ct);

        Assert.True(result.IsSuccess);
        var workbook = fixture.Writer.Request!.Sheets[0];
        Assert.Equal(new FormWorkbookCell("text", value), workbook.Rows[0][^1]);
        // Fifteen digits still fit.
        Assert.Equal(type == "Integer" ? 123456789012345L : (object)123456789012345m, workbook.Rows[1][^1].Value);
        var imported = FormCatalogImportPolicy.Build(new(id, 1, Enum.Parse<BusinessFormCatalogValueType>(type), "es", fixture.Reader.Items),
            FormCatalogExportSheet.Read(workbook), FormCatalogImportMode.Replace, culture);
        Assert.True(imported.IsSuccess);
        Assert.Empty(imported.Value.Preview.Issues);
        Assert.Equal(2, imported.Value.Preview.Counts.Unchanged);
        Assert.Empty(imported.Value.Items);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task A_date_before_Excel_dates_is_exported_as_its_exact_text(string culture)
    {
        using var scope = new CultureScope(culture);
        var fixture = Create();
        fixture.Reader.Catalog = fixture.Reader.Catalog! with { ValueType = "Date" };
        var id = fixture.Reader.Catalog.Id;
        fixture.Reader.Items = [new(Guid.NewGuid(), id, "A", "Fundación", null, null, null, 1, true, "1899-12-31"),
            new(Guid.NewGuid(), id, "B", "Siglo", null, null, null, 2, true, "1900-01-01")];

        var result = await fixture.Service.ExportAsync(id, false, Ct);

        Assert.True(result.IsSuccess);
        var workbook = fixture.Writer.Request!.Sheets[0];
        Assert.Equal(new FormWorkbookCell("text", "1899-12-31"), workbook.Rows[0][^1]);
        Assert.Equal(new DateTime(1900, 1, 1), workbook.Rows[1][^1].Value);
        var imported = FormCatalogImportPolicy.Build(new(id, 1, BusinessFormCatalogValueType.Date, "es", fixture.Reader.Items),
            FormCatalogExportSheet.Read(workbook), FormCatalogImportMode.Replace, culture);
        Assert.True(imported.IsSuccess);
        Assert.Empty(imported.Value.Preview.Issues);
        Assert.Equal(2, imported.Value.Preview.Counts.Unchanged);
    }

    [Theory]
    [InlineData("es", BusinessFormCatalogValueType.Text, 4)]
    [InlineData("en", BusinessFormCatalogValueType.Text, 6)]
    [InlineData("es", BusinessFormCatalogValueType.Integer, 5)]
    [InlineData("en", BusinessFormCatalogValueType.Integer, 7)]
    [InlineData("es", BusinessFormCatalogValueType.Decimal, 5)]
    [InlineData("en", BusinessFormCatalogValueType.Date, 7)]
    public async Task Template_has_only_the_localized_rules_row_and_translation_columns_only_for_the_other_language(string culture,
        BusinessFormCatalogValueType type, int count)
    {
        using var scope = new CultureScope(culture);
        var fixture = Create(); fixture.Reader.Catalog = fixture.Reader.Catalog! with { ValueType = type.ToString() };

        var result = await fixture.Service.ExportAsync(fixture.Reader.Catalog.Id, true, Ct);

        Assert.True(result.IsSuccess);
        string[] keys = culture == "es" ? ["code", "text", "detail", "active"] : ["code", "text", "textEn", "detail", "detailEn", "active"];
        var columns = FormCatalogImportTemplate.Columns(type, culture, "es", translation: culture == "en");
        Assert.Equal(count, columns.Count);
        Assert.Equal(keys, columns.Take(keys.Length).Select(column => column.Key));
        Assert.Equal(columns.Select(column => column.Header), fixture.Writer.Request!.Sheets[0].Headers);
        var row = Assert.Single(fixture.Writer.Request.Sheets[0].Rows);
        Assert.Equal(columns.Select(column => column.Rule), row.Select(cell => cell.Value));
        Assert.Equal("", row[0].Value);
        Assert.All(columns.Skip(1), column => Assert.False(string.IsNullOrWhiteSpace(column.Rule)));
        Assert.Equal(0, fixture.Reader.AllItemsReads);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Exporting_an_English_original_catalog_writes_the_Spanish_translation_only_with_the_menu_in_Spanish(string culture)
    {
        using var scope = new CultureScope(culture);
        var fixture = Create();
        fixture.Reader.Catalog = fixture.Reader.Catalog! with { OriginalCulture = "en" };
        var id = fixture.Reader.Catalog.Id;
        fixture.Reader.Items = [new(Guid.NewGuid(), id, "N", "Norte", "North", "Detalle norte", "North detail", 1, true),
            new(Guid.NewGuid(), id, "E", null, "East", null, null, 2, false)];

        var result = await fixture.Service.ExportAsync(id, false, Ct);

        Assert.True(result.IsSuccess);
        var workbook = fixture.Writer.Request!.Sheets[0];
        if (culture == "es")
        {
            Assert.Equal(["Código", "Texto", "Texto en español", "Detalle", "Detalle en español", "Activa"], workbook.Headers);
            Assert.Equal(new object?[] { "N", "North", "Norte", "North detail", "Detalle norte", "Sí" }, workbook.Rows[0].Select(cell => cell.Value));
            Assert.Equal(new object?[] { "E", "East", null, null, null, "No" }, workbook.Rows[1].Select(cell => cell.Value));
        }
        else
        {
            Assert.Equal(["Code", "Text", "Detail", "Active"], workbook.Headers);
            Assert.Equal(new object?[] { "N", "North", "North detail", "Yes" }, workbook.Rows[0].Select(cell => cell.Value));
            Assert.Equal(new object?[] { "E", "East", null, "No" }, workbook.Rows[1].Select(cell => cell.Value));
        }
    }

    [Theory]
    [InlineData("es", false, "Artículos.xlsx")]
    [InlineData("en", false, "Articles.xlsx")]
    [InlineData("es", true, "Artículos - plantilla.xlsx")]
    [InlineData("en", true, "Articles - template.xlsx")]
    public async Task The_file_is_named_after_the_catalog_in_the_menu_language(string culture, bool template, string expected)
    {
        using var scope = new CultureScope(culture);
        var fixture = Create();
        fixture.Reader.Catalog = fixture.Reader.Catalog! with { NameJson = """{"es":"Artículos","en":"Articles"}""" };

        var result = await fixture.Service.ExportAsync(fixture.Reader.Catalog.Id, template, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.FileName);
    }

    [Theory]
    [InlineData("en", """{"es":"Rubros"}""", "Rubros.xlsx")]
    [InlineData("es", """{"es":"Lotes 2025/26"}""", "Lotes 2025-26.xlsx")]
    public async Task The_file_name_falls_back_to_the_other_language_and_replaces_characters_a_file_cannot_have(string culture,
        string name, string expected)
    {
        using var scope = new CultureScope(culture);
        var fixture = Create();
        fixture.Reader.Catalog = fixture.Reader.Catalog! with { NameJson = name };

        var result = await fixture.Service.ExportAsync(fixture.Reader.Catalog.Id, false, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.FileName);
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
    private static object Typed(string type, string value) => type switch
    {
        "Integer" => long.Parse(value, CultureInfo.InvariantCulture),
        "Decimal" => decimal.Parse(value, CultureInfo.InvariantCulture),
        _ => DateTime.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
    };
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentUICulture;
        public CultureScope(string culture) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        public void Dispose() => CultureInfo.CurrentUICulture = previous;
    }
}
