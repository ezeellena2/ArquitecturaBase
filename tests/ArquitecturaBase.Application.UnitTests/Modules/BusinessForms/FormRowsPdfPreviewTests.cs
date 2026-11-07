using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormRowsPdfPreviewTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task The_preview_fills_the_block_with_two_items_of_examples_and_its_questions_only_inside(string culture)
    {
        var fixture = new FormPdfFixture(); var definition = fixture.Rules.Definition;
        static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
        var group = new CanonicalFormField(Guid.NewGuid(), "items", "group", Text("Artículos", "Articles"), null, true, null, null, Rows: new(1, 3));
        var quantity = new CanonicalFormField(Guid.NewGuid(), "quantity", "decimal", Text("Cantidad", "Quantity"), null, true, null, null, Repeated: true);
        var note = new CanonicalFormField(Guid.NewGuid(), "note", "shortText", Text("Observación", "Note"), null, false, 80, null, Repeated: true);
        Assert.True(definition.SetDraft(definition.Revision, definition.Name, definition.NameJson, definition.Prefix,
            definition.Culture, definition.SubmitLabelJson, FormDraftPolicy.Serialize(new FormQuestionSchema(1, [group, quantity, note])), 2, true).IsSuccess);
        var design = new FormPdfDesign(Text("Constancia", "Receipt"), [group.FieldId], false, null, true);

        var result = await fixture.Service.PreviewAsync(definition.Id, new(design, culture), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess); await result.Value.Content.DisposeAsync();
        using var values = JsonDocument.Parse(fixture.Rendered!.Response.Record.ValuesJson);
        var block = Assert.Single(values.RootElement.EnumerateArray());
        Assert.Equal(group.FieldId, block.GetProperty("fieldId").GetGuid());
        var items = block.GetProperty("value").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.All(items, item => Assert.Equal([quantity.FieldId, note.FieldId], item.EnumerateArray().Select(value => value.GetProperty("fieldId").GetGuid())));
        Assert.Equal(12500.5m, items[0][0].GetProperty("value").GetDecimal());
        Assert.Equal(0, fixture.Rules.UnitOfWork.Transactions); Assert.Empty(fixture.Designs);
    }
}
