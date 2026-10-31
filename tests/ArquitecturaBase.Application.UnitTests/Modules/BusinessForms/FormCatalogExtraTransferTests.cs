using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogExtraTransferTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Exported_extra_values_are_typed_cells_and_reimport_without_differences_or_precision_loss(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var (service, reader, writer) = Export();
            var fields = new[] { Field("stock", "integer"), Field("price", "decimal") with { Position = 1 }, Field("since", "date") with { Position = 2 },
                Field("flag", "boolean") with { Position = 3 }, Field("note", "text") with { Position = 4, MaxLength = 300 } };
            reader.Catalog = reader.Catalog! with { FieldsJson = JsonSerializer.Serialize(fields, JsonSerializerOptions.Web) };
            var id = reader.Catalog.Id;
            reader.Items = [Item(id, "A", 1, Values(fields, 1500L, "1234.5", "2026-10-22", true, "=2+2")),
                Item(id, "B", 2, Values(fields, 123456789012345678L, "12345678.123456789", "1850-05-01", false, null))];

            var result = await service.ExportAsync(id, false, Ct);

            Assert.True(result.IsSuccess);
            var workbook = writer.Request!;
            // The catalog is Spanish, so the English columns appear only with the menu in English.
            var fixedColumns = culture == "es" ? 4 : 6;
            Assert.Equal(["stock", "price", "since", "flag", "note"], workbook.Headers.Skip(fixedColumns));
            Assert.Equal([new("integer", 1500L, "#,##0"), new("decimal", 1234.5m, "#,##0.0"),
                new("date", new DateTime(2026, 10, 22), culture == "es" ? "dd/mm/yyyy" : "mm/dd/yyyy"),
                new("text", culture == "es" ? "Sí" : "Yes"), new FormWorkbookCell("text", "=2+2")], workbook.Rows[0].Skip(fixedColumns));
            // Beyond 15 digits, or before 1900, Excel cannot hold the value: it goes as its exact text.
            Assert.Equal([new("text", "123456789012345678"), new("text", "12345678.123456789"), new("text", "1850-05-01"),
                new("text", "No"), new FormWorkbookCell("text", null)], workbook.Rows[1].Skip(fixedColumns));
            var imported = FormCatalogImportPolicy.Build(new(id, reader.Catalog.Revision, BusinessFormCatalogValueType.Text, reader.Catalog.OriginalCulture,
                reader.Items, Fields: fields), FormCatalogExportSheet.Read(workbook), FormCatalogImportMode.Replace, culture);
            Assert.True(imported.IsSuccess);
            Assert.Empty(imported.Value.Preview.Issues);
            Assert.Empty(imported.Value.Items);
            Assert.Equal(2, imported.Value.Preview.Counts.Unchanged);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Extra_columns_are_named_in_the_menu_language_or_else_in_the_other_one(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var (service, reader, writer) = Export();
            var fields = new[] { Field("rubro", "text", ("es", "Rubro"), ("en", "Category")),
                Field("habitual", "text", ("es", "Proveedor habitual")) with { Position = 1 } };
            reader.Catalog = reader.Catalog! with { FieldsJson = JsonSerializer.Serialize(fields, JsonSerializerOptions.Web) };
            var id = reader.Catalog.Id;
            reader.Items = [Item(id, "A", 1, Values(fields, "Insumos", "Sí"))];

            var result = await service.ExportAsync(id, false, Ct);

            Assert.True(result.IsSuccess);
            var workbook = writer.Request!;
            Assert.Equal(culture == "es" ? ["Rubro", "Proveedor habitual"] : ["Category", "Proveedor habitual"], workbook.Headers.TakeLast(2));
            var imported = FormCatalogImportPolicy.Build(new(id, reader.Catalog.Revision, BusinessFormCatalogValueType.Text, reader.Catalog.OriginalCulture,
                reader.Items, Fields: fields), FormCatalogExportSheet.Read(workbook), FormCatalogImportMode.Replace, culture);
            Assert.True(imported.IsSuccess);
            Assert.Empty(imported.Value.Preview.Issues);
            Assert.Equal(1, imported.Value.Preview.Counts.Unchanged);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    // A name that the import would read as a fixed column or as another field goes with [campo]/[field]; one still ambiguous, by its key.
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Extra_columns_whose_name_repeats_a_fixed_column_or_another_field_stay_recognizable(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var (service, reader, writer) = Export();
            var fields = new[]
            {
                Field("detalle_extra", "text", ("es", "Detalle"), ("en", "Detail")),
                Field("zona_a", "text", ("es", "Zona"), ("en", "Zone")) with { Position = 1 },
                Field("zona_b", "text", ("es", "Zona"), ("en", "Zone")) with { Position = 2 },
                Field("region", "text", ("es", "ciudad"), ("en", "City")) with { Position = 3 },
                Field("ciudad", "text", ("es", "Ciudad del lote"), ("en", "Lot city")) with { Position = 4 },
                Field("importe", "text", ("es", "Valor (USD)"), ("en", "Value (USD)")) with { Position = 5 },
            };
            reader.Catalog = reader.Catalog! with { FieldsJson = JsonSerializer.Serialize(fields, JsonSerializerOptions.Web) };
            var id = reader.Catalog.Id;
            reader.Items = [Item(id, "A", 1, Values(fields, "Largo", "Norte", "Sur", "Pergamino", "Rojas", "15"))];

            var result = await service.ExportAsync(id, false, Ct);

            Assert.True(result.IsSuccess);
            var workbook = writer.Request!;
            Assert.Equal(culture == "es"
                ? ["Detalle [campo]", "zona_a", "zona_b", "ciudad [campo]", "Ciudad del lote", "Valor (USD) [campo]"]
                : ["Detail [field]", "zona_a", "zona_b", "City", "Lot city", "Value (USD) [field]"], workbook.Headers.TakeLast(6));
            var imported = FormCatalogImportPolicy.Build(new(id, reader.Catalog.Revision, BusinessFormCatalogValueType.Text, reader.Catalog.OriginalCulture,
                reader.Items, Fields: fields), FormCatalogExportSheet.Read(workbook), FormCatalogImportMode.Replace, culture);
            Assert.True(imported.IsSuccess);
            Assert.Empty(imported.Value.Preview.Issues);
            Assert.Equal(1, imported.Value.Preview.Counts.Unchanged);
            Assert.Empty(imported.Value.Items);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    // Read as an old «clave [canónico]» header, the column would take only true/false: the name goes with [campo]/[field].
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task A_field_named_like_an_old_canonical_header_still_exports_yes_or_no_that_reimports(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var (service, reader, writer) = Export();
            var field = Field("propio", "boolean", ("es", "propio [canónico]"), ("en", "propio [canonical]"));
            reader.Catalog = reader.Catalog! with { FieldsJson = JsonSerializer.Serialize(new[] { field }, JsonSerializerOptions.Web) };
            var id = reader.Catalog.Id;
            reader.Items = [Item(id, "A", 1, Values([field], true)), Item(id, "B", 2, Values([field], false))];

            var result = await service.ExportAsync(id, false, Ct);

            Assert.True(result.IsSuccess);
            var workbook = writer.Request!;
            Assert.Equal(culture == "es" ? "propio [canónico] [campo]" : "propio [canonical] [field]", workbook.Headers[^1]);
            var imported = FormCatalogImportPolicy.Build(new(id, reader.Catalog.Revision, BusinessFormCatalogValueType.Text, reader.Catalog.OriginalCulture,
                reader.Items, Fields: [field]), FormCatalogExportSheet.Read(workbook), FormCatalogImportMode.Replace, culture);
            Assert.True(imported.IsSuccess);
            Assert.Empty(imported.Value.Preview.Issues);
            Assert.Equal(2, imported.Value.Preview.Counts.Unchanged);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    // The option's text in the menu language; if reimporting it would pick another option, its code, and only then its identity.
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Source_options_export_their_text_and_reimport_unchanged_when_repeated_or_deactivated(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var (service, reader, writer) = Export();
            var sourceId = Guid.CreateVersion7();
            var field = Field("tipo", "catalog", ("es", "Tipo de producto"), ("en", "Product type")) with { SourceCatalogId = sourceId };
            FormCatalogStoredItemRow supplies = Option(sourceId, "INS-1", "Insumos", "Supplies", 0, true),
                inputs = Option(sourceId, "INS-2", "Insumos", "Inputs", 1, true), seeds = Option(sourceId, "SEM", "Semillas", "Seeds", 2, false),
                oldGrains = Option(sourceId, "GRA-OLD", "Granos", "Grains", 3, false), grains = Option(sourceId, "GRA", "Granos", "Grains", 4, true),
                hidden = Option(sourceId, "Granos", "Granos", "Grains", 5, false);
            FormCatalogStoredItemRow[] options = [supplies, inputs, seeds, oldGrains, grains, hidden];
            reader.SourceItems[sourceId] = options;
            reader.Catalog = reader.Catalog! with { FieldsJson = JsonSerializer.Serialize(new[] { field }, JsonSerializerOptions.Web) };
            var id = reader.Catalog.Id;
            reader.Items = options.Select((option, index) => Item(id, "P" + index, index, Values([field], option.Id.ToString("D")))).ToArray();

            var result = await service.ExportAsync(id, false, Ct);

            Assert.True(result.IsSuccess);
            var workbook = writer.Request!;
            Assert.Equal(culture == "es" ? "Tipo de producto" : "Product type", workbook.Headers[^1]);
            Assert.Equal(culture == "es" ? ["Insumos", "Insumos", "Semillas", "GRA-OLD", "Granos", hidden.Id.ToString("D")]
                : ["Supplies", "Inputs", "Seeds", "GRA-OLD", "Grains", hidden.Id.ToString("D")], workbook.Rows.Select(row => row[^1].Value));
            var imported = FormCatalogImportPolicy.Build(new(id, reader.Catalog.Revision, BusinessFormCatalogValueType.Text, reader.Catalog.OriginalCulture,
                reader.Items, Fields: [field], Sources: reader.SourceItems), FormCatalogExportSheet.Read(workbook), FormCatalogImportMode.Replace, culture);
            Assert.True(imported.IsSuccess);
            Assert.Empty(imported.Value.Preview.Issues);
            Assert.Equal(6, imported.Value.Preview.Counts.Unchanged);
            Assert.Empty(imported.Value.Items);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public async Task Import_apply_uses_the_shared_field_recorder_and_returns_current_metadata()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es");
        try
        {
            using var fixture = new FormCatalogImportFixture();
            var field = Field("amount", "decimal");
            Assert.True(fixture.Catalog.SetFields([field], null).IsSuccess);
            fixture.Sheet = new("Extra.csv", FormCatalogImportFixture.Hash,
                [FormCatalogImportFixture.Row(1, "Code", "Text", "amount"), FormCatalogImportFixture.Row(2, "NEW", "Opción", "1.500,50")]);

            var preview = await fixture.Service.PreviewAsync(fixture.Catalog.Id, fixture.Request(), Ct);
            Assert.True(preview.IsSuccess);
            Assert.Equal("1500.5", Assert.Single(preview.Value.Added).Fields![field.Id.ToString("D")]);
            var applied = await fixture.Service.ApplyAsync(fixture.Catalog.Id, fixture.Request(), Ct);

            Assert.True(applied.IsSuccess);
            Assert.Equal("1500.5", JsonDocument.Parse(Assert.Single(fixture.Items).FieldValuesJson).RootElement.GetProperty(field.Id.ToString("D")).GetString());
            Assert.Equal(2, fixture.Catalog.Revision);
            Assert.Equal(1, fixture.Work.Commits);
            Assert.NotNull(applied.Value.FieldIdentityMap);
            Assert.Empty(applied.Value.FieldIdentityMap);
            Assert.Equal(1, Assert.Single(applied.Value.Catalog.Fields!).ValuesCount);
            Assert.Equal(2, fixture.FileReads);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public async Task Import_apply_clears_a_value_under_a_present_empty_column_and_keeps_an_absent_column()
    {
        using var fixture = new FormCatalogImportFixture();
        var (cleared, kept) = (Field("amount", "text") with { MaxLength = 300 }, Field("note", "text") with { MaxLength = 300, Position = 1 });
        Assert.True(fixture.Catalog.SetFields([cleared, kept], null).IsSuccess);
        var item = fixture.Add("OLD", "Viejo");
        Assert.True(fixture.Catalog.SetManualItemFields(item, JsonSerializer.Serialize(new Dictionary<string, string>
            { [cleared.Id.ToString("D")] = "before", [kept.Id.ToString("D")] = "kept" }, JsonSerializerOptions.Web)).IsSuccess);
        fixture.Sheet = new("Extra.csv", FormCatalogImportFixture.Hash,
            [FormCatalogImportFixture.Row(1, "Code", "Text", "amount"), FormCatalogImportFixture.Row(2, "OLD", "Viejo", "")]);

        var applied = await fixture.Service.ApplyAsync(fixture.Catalog.Id, fixture.Request(), Ct);

        Assert.True(applied.IsSuccess, applied.IsSuccess ? null : applied.Error.Code);
        var values = FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson);
        Assert.False(values.ContainsKey(cleared.Id.ToString("D")));
        Assert.Equal("kept", values[kept.Id.ToString("D")].GetString());
    }

    private static BusinessFormCatalogField Field(string key, string type) => new(Guid.CreateVersion7(), key,
        new Dictionary<string, string> { ["es"] = key, ["en"] = key }, type, null, false, false, null, null, null, "data", 0, true);

    private static BusinessFormCatalogField Field(string key, string type, params (string Culture, string Name)[] names) =>
        Field(key, type) with { Name = names.ToDictionary(name => name.Culture, name => name.Name), MaxLength = type == "text" ? 300 : null };

    private static (FormCatalogExportService Service, FormCatalogExportReader Reader, FakeFormWorkbookWriter Writer) Export()
    {
        var actor = Guid.CreateVersion7();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead];
        var reader = new FormCatalogExportReader();
        var writer = new FakeFormWorkbookWriter();
        return (new(reader, writer, new(new FakeCurrentUser { UserId = actor }, permissions)), reader, writer);
    }

    private static FormCatalogStoredItemRow Item(Guid catalogId, string code, int position, string values) =>
        new(Guid.CreateVersion7(), catalogId, code, "Opción " + code, null, null, null, position, true, FieldValuesJson: values);

    // The stored values of the fields, in their order; null leaves a field without value.
    private static string Values(IReadOnlyList<BusinessFormCatalogField> fields, params object?[] values) =>
        JsonSerializer.Serialize(fields.Zip(values).Where(pair => pair.Second is not null)
            .ToDictionary(pair => pair.First.Id.ToString("D"), pair => pair.Second), JsonSerializerOptions.Web);

    private static FormCatalogStoredItemRow Option(Guid catalogId, string code, string spanish, string english, int position, bool active) =>
        new(Guid.CreateVersion7(), catalogId, code, spanish, english, null, null, position, active);
}
