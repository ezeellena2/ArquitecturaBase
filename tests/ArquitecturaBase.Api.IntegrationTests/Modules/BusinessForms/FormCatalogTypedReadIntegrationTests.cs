using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Empty_typed_catalog_response_sheets_load_types_once_and_export_their_value_headers()
    {
        using var exporter = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport]);
        exporter.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        var types = new[] { BusinessFormCatalogValueType.Integer, BusinessFormCatalogValueType.Decimal,
            BusinessFormCatalogValueType.Date, BusinessFormCatalogValueType.Text };
        var catalogs = types.Select(type => BusinessFormCatalog.CreateManual("es", 
            new Dictionary<string, string> { ["es"] = type.ToString(), ["en"] = type.ToString() },
            new Dictionary<string, string>(), type)).ToArray();
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.AddRange(catalogs);
            await db.SaveChangesAsync(Ct);
        });
        var questions = catalogs.Select((catalog, index) => Field("choice", false) with
        {
            Label = new Dictionary<string, string> { ["es"] = "Tipo " + types[index], ["en"] = "Type " + types[index] },
            Source = new(catalog.Id), Options = null
        }).ToArray();
        var repeated = Field("choice", false) with
        {
            Label = new Dictionary<string, string> { ["es"] = "Otro entero", ["en"] = "Other integer" },
            Source = new(catalogs[0].Id), Options = null
        };
        var seeded = await SeedSheetAsync(exporter, 0, [.. questions, repeated]);
        await factory.ExecuteScopeAsync(async services =>
        {
            var recorder = new FormResponseCommandRecorder();
            await using var db = new TestDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(
                services.GetRequiredService<DbContextOptions<ApplicationDbContext>>()).AddInterceptors(recorder).Options);
            var reader = new FormResponseReader(db, new FormRecordReader(db, services.GetRequiredService<IFormPublicationProtector>()));

            var definition = await reader.FindDefinitionAsync(seeded.Id, Ct);

            Assert.NotNull(definition);
            Assert.NotNull(definition.CatalogValueTypes);
            Assert.Equal(4, definition.CatalogValueTypes.Count);
            foreach (var (catalog, type) in catalogs.Zip(types)) Assert.Equal(type.ToString(), definition.CatalogValueTypes[catalog.Id]);
            var metadata = Assert.Single(recorder.Commands, command => command.Sql.Contains("\"BusinessFormCatalogs\"", StringComparison.Ordinal));
            Assert.Contains("\"ValueType\"", metadata.Sql, StringComparison.Ordinal);
            var columns = FormResponsePresentationPolicy.Columns(definition, "en");
            Assert.Equal(new[] { "Integer", "Decimal", "Date", null, "Integer" },
                columns.Where(column => questions.Any(question => question.FieldId == column.FieldId) || column.FieldId == repeated.FieldId)
                    .Select(column => column.ValueType));
        });

        using var response = await exporter.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/records/export",
            new { filter = new { }, columns = questions.Select(question => question.FieldId).Append(repeated.FieldId).ToArray() }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(Ct);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        Assert.Equal(new[] { "Number", "Received (UTC)", "Submitted by", "Channel", "Type Integer", "Type Integer · value",
            "Type Decimal", "Type Decimal · value", "Type Date", "Type Date · value", "Type Text", "Other integer", "Other integer · value" },
            sheet.Row(1).CellsUsed().Select(cell => cell.GetString()));
        Assert.Equal(1, sheet.LastRowUsed()!.RowNumber());
    }
}
