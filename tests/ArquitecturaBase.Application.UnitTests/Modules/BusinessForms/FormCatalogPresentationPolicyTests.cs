using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogPresentationPolicyTests
{
    [Fact]
    public void An_inherited_presentation_keeps_the_original_text_and_detail()
    {
        var result = FormCatalogPresentationPolicy.Compose(null,
            new Dictionary<string, string?> { ["text"] = "Equipo", ["detail"] = "Detalle" });
        Assert.Equal("Equipo", result.Title);
        Assert.Equal("Detalle", result.Description);
    }

    [Fact]
    public void Ordered_parts_skip_absent_empty_and_whitespace_values_without_extra_separators()
    {
        var result = FormCatalogPresentationPolicy.Compose(new(["code", "missing", "blank", "text"], ["empty", "field"]),
            new Dictionary<string, string?> { ["code"] = "A1", ["text"] = "Equipo", ["blank"] = "  ", ["empty"] = null, ["field"] = "Dato" });
        Assert.Equal("A1 · Equipo", result.Title);
        Assert.Equal("Dato", result.Description);
    }

    [Fact]
    public void An_empty_description_is_omitted_and_an_empty_title_is_invalid()
    {
        var result = FormCatalogPresentationPolicy.Compose(new(["missing"], []), new Dictionary<string, string?>());
        Assert.Equal("", result.Title);
        Assert.Null(result.Description);
        Assert.False(FormCatalogPresentationPolicy.IsValid(result));
    }

    [Theory]
    [InlineData(30, 300, true)]
    [InlineData(31, 300, false)]
    [InlineData(30, 301, false)]
    public void The_composed_value_respects_the_channel_title_and_description_limits(int title, int description, bool expected)
    {
        var result = FormCatalogPresentationPolicy.Compose(new(["text"], ["detail"]),
            new Dictionary<string, string?> { ["text"] = new('t', title), ["detail"] = new('d', description) });
        Assert.Equal(title, result.Title.Length);
        Assert.Equal(description, result.Description!.Length);
        Assert.Equal(expected, FormCatalogPresentationPolicy.IsValid(result));
    }
}
