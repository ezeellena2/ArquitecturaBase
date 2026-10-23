using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogSourceValidationTests
{
    [Theory]
    [InlineData("empty-id")]
    [InlineData("duplicate")]
    [InlineData("both")]
    [InlineData("neither")]
    [InlineData("blank")]
    [InlineData("empty-person")]
    [InlineData("too-many")]
    public async Task Invalid_match_shapes_are_reported_on_the_question_source(string problem)
    {
        var id = Guid.CreateVersion7();
        FormCatalogMatch[] matches = problem switch
        {
            "empty-id" => [new(Guid.Empty, "fixed")],
            "duplicate" => [new(id, "fixed"), new(id, "other")],
            "both" => [new(id, "fixed", Guid.CreateVersion7())],
            "neither" => [new(id)],
            "blank" => [new(id, " ")],
            "empty-person" => [new(id, PersonAttributeId: Guid.Empty)],
            _ => Enumerable.Range(0, 21).Select(_ => new FormCatalogMatch(Guid.CreateVersion7(), "fixed")).ToArray(),
        };
        var result = await new SaveFormDraftRequestValidator().ValidateAsync(Input(new(Guid.CreateVersion7(), matches)),
            TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName.StartsWith("draft.fields[0].source.match", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("empty-title")]
    [InlineData("invalid-part")]
    [InlineData("too-many")]
    public async Task Invalid_presentation_shapes_are_reported_on_the_question_source(string problem)
    {
        FormCatalogPresentation presentation = problem switch
        {
            "empty-title" => new([], ["detail"]),
            "invalid-part" => new(["unknown"], ["detail"]),
            _ => new(Enumerable.Repeat("text", 25).ToArray(), []),
        };
        var result = await new SaveFormDraftRequestValidator().ValidateAsync(Input(new(Guid.CreateVersion7(), Presentation: presentation)),
            TestContext.Current.CancellationToken);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName.StartsWith("draft.fields[0].source.presentation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Valid_fixed_and_person_filters_and_an_overflowing_composed_title_can_be_saved()
    {
        var source = new FormQuestionSource(Guid.CreateVersion7(), [new(Guid.CreateVersion7(), "fixed"),
            new(Guid.CreateVersion7(), PersonAttributeId: Guid.CreateVersion7())], new(["text", "code"], ["detail"]));
        var result = await new SaveFormDraftRequestValidator().ValidateAsync(Input(source), TestContext.Current.CancellationToken);
        Assert.True(result.IsValid);
    }

    private static SaveFormDraftRequest Input(FormQuestionSource source) => new(Guid.CreateVersion7(),
        new Dictionary<string, string> { ["es"] = "Formulario" }, "CAT", new(1, "es", null,
            [new("question", null, null, "choice", new Dictionary<string, string> { ["es"] = "Opciones" }, null, true, null, null, source)]));
}
