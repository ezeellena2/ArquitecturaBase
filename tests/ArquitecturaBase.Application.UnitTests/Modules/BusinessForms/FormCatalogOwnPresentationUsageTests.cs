using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogOwnPresentationUsageTests
{
    [Fact]
    public async Task A_field_selected_by_the_catalog_presentation_is_in_use_with_its_own_scope()
    {
        var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
        var field = new BusinessFormCatalogField(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Field" }, "text", null, false, false, null, null, 300, "data", 0, true);
        var presentation = new FormCatalogPresentation(["text", field.Id.ToString("D")], []);
        Assert.True(catalog.SetFields([field], JsonSerializer.Serialize(presentation, JsonSerializerOptions.Web)).IsSuccess);
        var input = new FormCatalogFieldSaveRequest(null, field.Id, field.Key, new Dictionary<string, string?> { ["es"] = "Field" }, field.Type, null,
            false, false, null, null, 300, "data", 0, false);
        var result = await new FormCatalogFieldRecorder(new FormCatalogFieldReader()).PrepareAsync(catalog, [], [input], presentation, [], true, TestContext.Current.CancellationToken);
        Assert.Equal("Forms.Catalog.FieldInUse", result.Error.Code);
        var use = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<FormCatalogFieldUsageResponse>>(result.Error.Metadata!["uses"]));
        Assert.Equal("Catalog", use.Stage);
        Assert.Null(use.DefinitionId);
        Assert.Null(use.Question);
        Assert.Null(use.QuestionId);
    }

    [Fact]
    public async Task The_own_presentation_use_names_the_catalog_in_its_original_when_the_menu_language_is_missing()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-AR");
        try
        {
            var names = new Dictionary<string, string> { ["en"] = "Branches" };
            var catalog = BusinessFormCatalog.CreateManual("en", names, new Dictionary<string, string>());
            var field = new BusinessFormCatalogField(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["en"] = "Field" }, "text", null, false, false, null, null, 300, "data", 0, true);
            var presentation = new FormCatalogPresentation(["text", field.Id.ToString("D")], []);
            Assert.True(catalog.SetFields([field], JsonSerializer.Serialize(presentation, JsonSerializerOptions.Web)).IsSuccess);
            var detail = new FormCatalogDetailResponse(catalog.Id, catalog.Key, names, new Dictionary<string, string>(), catalog.OriginalCulture, "Manual", 1, true, 0, null, null);
            var input = new FormCatalogFieldSaveRequest(null, field.Id, field.Key, new Dictionary<string, string?> { ["en"] = "Field" }, field.Type, null,
                false, false, null, null, 300, "data", 0, false);

            var described = await FormCatalogFieldPresentation.DescribeAsync(detail, catalog.FieldsJson, catalog.PresentationJson, [],
                new FormCatalogFieldReader(), true, TestContext.Current.CancellationToken);
            var removal = await new FormCatalogFieldRecorder(new FormCatalogFieldReader()).PrepareAsync(catalog, [], [input], presentation, [], true,
                TestContext.Current.CancellationToken);

            Assert.Equal("Branches", Assert.Single(Assert.Single(described.Fields!).UsedBy).FormName);
            Assert.Equal("Forms.Catalog.FieldInUse", removal.Error.Code);
            Assert.Equal("Branches", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<FormCatalogFieldUsageResponse>>(removal.Error.Metadata!["uses"])).FormName);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
