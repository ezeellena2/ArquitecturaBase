using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormMultipleChoicePdfPreviewTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Preview_supplies_a_real_selection_array_within_its_bounds_without_saving(string culture)
    {
        var fixture = new FormPdfFixture(); var definition = fixture.Rules.Definition;
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var field = new CanonicalFormField(Guid.NewGuid(), "risks", "multiChoice",
            new Dictionary<string, string> { ["es"] = "Riesgos", ["en"] = "Risks" }, null, true, null,
            [new(first, new Dictionary<string, string> { ["es"] = "Frío", ["en"] = "Cold" }),
             new(second, new Dictionary<string, string> { ["es"] = "Ruido", ["en"] = "Noise" })], Selection: new(2, 3));
        Assert.True(definition.SetDraft(definition.Revision, definition.Name, definition.NameJson, definition.Prefix,
            definition.Culture, definition.SubmitLabelJson, FormDraftPolicy.Serialize(new FormQuestionSchema(1, [field])), 1, true).IsSuccess);
        var design = new FormPdfDesign(new Dictionary<string, string> { ["es"] = "Constancia", ["en"] = "Receipt" }, [field.FieldId], false, null, true);
        var result = await fixture.Service.PreviewAsync(definition.Id, new(design, culture), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); await result.Value.Content.DisposeAsync();
        using var values = JsonDocument.Parse(fixture.Rendered!.Response.Record.ValuesJson);
        var answer = Assert.Single(values.RootElement.EnumerateArray());
        Assert.Equal(field.FieldId, answer.GetProperty("fieldId").GetGuid());
        Assert.Equal(JsonValueKind.Array, answer.GetProperty("value").ValueKind);
        Assert.Equal(new[] { first, second }, answer.GetProperty("value").EnumerateArray().Select(value => value.GetGuid()));
        Assert.Equal(0, fixture.Rules.UnitOfWork.Transactions); Assert.Empty(fixture.Designs);
        Assert.Null(definition.ActivePdfDesignId);
    }
}
