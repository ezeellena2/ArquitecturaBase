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
    public async Task Exported_extra_values_reimport_without_differences_or_precision_loss(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var actor = Guid.CreateVersion7();
            var current = new FakeCurrentUser { UserId = actor };
            var permissions = new FakePermissionService();
            permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead];
            var reader = new FormCatalogExportReader();
            var writer = new FakeFormWorkbookWriter();
            var service = new FormCatalogExportService(reader, writer, new(current, permissions));
            var fields = new[] { Field("amount", "decimal"), Field("days", "integer"), Field("note", "text") };
            reader.Catalog = reader.Catalog! with { FieldsJson = JsonSerializer.Serialize(fields, JsonSerializerOptions.Web) };
            var id = reader.Catalog.Id;
            reader.Items = [new(Guid.CreateVersion7(), id, "A", "Opción", null, null, null, 1, true,
                FieldValuesJson: JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    [fields[0].Id.ToString("D")] = "0.001", [fields[1].Id.ToString("D")] = 123456789012345678L,
                    [fields[2].Id.ToString("D")] = "=2+2",
                }))];

            var result = await service.ExportAsync(id, false, Ct);

            Assert.True(result.IsSuccess);
            var workbook = writer.Request!;
            Assert.Equal(9, workbook.Headers.Count);
            Assert.Equal(["0.001", "123456789012345678", "=2+2"], Assert.Single(workbook.Rows).Skip(6).Select(cell => cell.Value));
            Assert.All(workbook.Rows.SelectMany(row => row), cell => Assert.Equal("text", cell.Kind));
            var sheet = new FormSpreadsheet("Export.xlsx", new('f', 64),
                new[] { new FormSpreadsheetRow(1, workbook.Headers.Select(header => new FormSpreadsheetCell(header)).ToArray()) }
                    .Concat(workbook.Rows.Select((row, index) => new FormSpreadsheetRow(index + 2,
                        row.Select(cell => new FormSpreadsheetCell((string?)cell.Value)).ToArray()))).ToArray());
            var imported = FormCatalogImportPolicy.Build(new(id, reader.Catalog.Revision, BusinessFormCatalogValueType.Text, reader.Catalog.OriginalCulture, reader.Items, Fields: fields),
                sheet, FormCatalogImportMode.Replace, culture);
            Assert.True(imported.IsSuccess);
            Assert.Empty(imported.Value.Preview.Issues);
            Assert.Empty(imported.Value.Items);
            Assert.Equal(1, imported.Value.Preview.Counts.Unchanged);
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
            Assert.True(Assert.Single(applied.Value.Catalog.Fields!).HasValues);
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
}
