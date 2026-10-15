using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogCompatibilityTests
{
    [Theory]
    [InlineData("role")]
    [InlineData("active")]
    public async Task Only_name_is_allowed_as_the_option_text_while_other_columns_remain_details(string column)
    {
        var validator = new FormCatalogFilterPreviewRequestValidator();
        var filters = new FormCatalogFilterDefinition(column, column, [], false, column, false);
        var result = await validator.ValidateAsync(new FormCatalogFilterPreviewRequest(Guid.NewGuid(), filters), TestContext.Current.CancellationToken);
        Assert.Single(result.Errors, error => error.PropertyName == "filters.textColumn");
        Assert.DoesNotContain(result.Errors, error => error.PropertyName == "filters.detailColumn" || error.PropertyName == "filters.sortColumn");
        var valid = await validator.ValidateAsync(new FormCatalogFilterPreviewRequest(Guid.NewGuid(), filters with { TextColumn = "name" }), TestContext.Current.CancellationToken);
        Assert.True(valid.IsValid);
    }

    [Theory]
    [InlineData(31, 0, "items[0].text.es")]
    [InlineData(1, 301, "items[0].detail.es")]
    public async Task Manual_options_report_unrepresentable_text_at_the_editable_field(int textLength, int detailLength, string path)
    {
        var metadata = new Dictionary<string, string?> { ["es"] = "Personas", ["en"] = "People" };
        var text = new Dictionary<string, string?> { ["es"] = new('x', textLength), ["en"] = null };
        var detail = new Dictionary<string, string?> { ["es"] = new('x', detailLength), ["en"] = null };
        var request = new CreateManualCatalogRequest(metadata, metadata, [new("new-option", null, "CODE", text, detail, true)]);
        var validation = await new CreateManualCatalogRequestValidator().ValidateAsync(request, TestContext.Current.CancellationToken);
        Assert.Contains(validation.Errors, error => error.PropertyName == path);
    }

    [Theory]
    [InlineData(30, 300, true)]
    [InlineData(31, 0, false)]
    [InlineData(1, 301, false)]
    public void Preview_marks_incompatible_options_without_changing_their_text(int textLength, int detailLength, bool expected)
    {
        var option = new FormCatalogFilterOptionResponse(Guid.NewGuid(), new('x', textLength), new('x', detailLength));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(option, JsonSerializerOptions.Web));
        Assert.Equal(expected, json.RootElement.GetProperty("canSend").GetBoolean());
        Assert.Equal(textLength, option.Text.Length);
        Assert.Equal(detailLength, option.Detail!.Length);
    }
}
