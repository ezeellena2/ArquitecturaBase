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
        var catalog = BusinessFormCatalog.CreateManual(new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
        var field = new BusinessFormCatalogField(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Field" }, "text", null, false, false, null, null, 300, "data", 0, true);
        var presentation = new FormCatalogPresentation(["text", field.Id.ToString("D")], []);
        Assert.True(catalog.SetFields([field], JsonSerializer.Serialize(presentation, JsonSerializerOptions.Web)).IsSuccess);
        var input = new FormCatalogFieldSaveRequest(null, field.Id, field.Key, new Dictionary<string, string?> { ["es"] = "Field" }, field.Type, null,
            false, false, null, null, 300, "data", 0, false);
        var result = await new FormCatalogFieldRecorder(new FormCatalogFieldReader()).PrepareAsync(catalog, [], [input], presentation, [], TestContext.Current.CancellationToken);
        Assert.Equal("Forms.Catalog.FieldInUse", result.Error.Code);
        var use = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<FormCatalogFieldUsageResponse>>(result.Error.Metadata!["uses"]));
        Assert.Equal("Catalog", use.Stage);
        Assert.Null(use.DefinitionId);
        Assert.Null(use.Question);
    }
}
