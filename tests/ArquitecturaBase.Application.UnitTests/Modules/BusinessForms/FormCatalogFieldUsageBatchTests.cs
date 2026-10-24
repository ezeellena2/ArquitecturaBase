using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogFieldUsageBatchTests
{
    [Fact]
    public async Task Detail_loads_all_field_uses_in_one_batch()
    {
        var fields = new[] { Field("first"), Field("second") };
        var catalog = Catalog();
        Assert.True(catalog.SetFields(fields, null).IsSuccess);
        var reader = new FormCatalogFieldReader();
        var detail = new FormCatalogDetailResponse(catalog.Id, catalog.Key, fields[0].Name, new Dictionary<string, string>(), catalog.OriginalCulture, "Manual", 1, true, 0, null, null);
        var result = await FormCatalogFieldPresentation.DescribeAsync(detail, catalog.FieldsJson, null, [], reader, TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Fields!.Count);
        Assert.Equal(1, reader.UsageReads);
    }

    [Fact]
    public async Task Detail_reports_the_question_identity_of_each_field_use()
    {
        var field = Field("first");
        var catalog = Catalog();
        Assert.True(catalog.SetFields([field], null).IsSuccess);
        var definitionId = Guid.CreateVersion7();
        var questionId = Guid.CreateVersion7();
        var reader = new FormCatalogFieldReader();
        reader.Uses.Add(new(definitionId, "Form", "Question", questionId, "Draft", field.Id, ByMatch: true));
        var detail = new FormCatalogDetailResponse(catalog.Id, catalog.Key, field.Name, new Dictionary<string, string>(), catalog.OriginalCulture, "Manual", 1, true, 0, null, null);

        var result = await FormCatalogFieldPresentation.DescribeAsync(detail, catalog.FieldsJson, null, [], reader, TestContext.Current.CancellationToken);

        Assert.Equal(new FormCatalogFieldUsageResponse(definitionId, "Form", "Question", questionId, "Draft"),
            Assert.Single(Assert.Single(result.Fields!).UsedBy));
    }

    [Fact]
    public async Task Removing_several_fields_checks_uses_in_one_batch()
    {
        var catalog = Catalog();
        var fields = new[] { Field("first"), Field("second") };
        Assert.True(catalog.SetFields(fields, null).IsSuccess);
        var reader = new FormCatalogFieldReader();
        var inputs = fields.Select(field => (FormCatalogFieldSaveRequest?)new FormCatalogFieldSaveRequest(null, field.Id, field.Key,
            field.Name.ToDictionary(pair => pair.Key, pair => (string?)pair.Value), field.Type, null, false, false, null, null, 300, "data", field.Position, false)).ToArray();
        var result = await new FormCatalogFieldRecorder(reader).PrepareAsync(catalog, [], inputs, null, [], TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, reader.UsageReads);
    }

    private static BusinessFormCatalog Catalog() => BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
    private static BusinessFormCatalogField Field(string key) => new(Guid.CreateVersion7(), key, new Dictionary<string, string> { ["es"] = key }, "text", null, false, false, null, null, 300, "data", 0, true);
}
