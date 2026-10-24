using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

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

    // The WhatsApp title shows a typed value with the same screen format as the front (the PDF has its own): grouped numbers,
    // a numeric date and Yes/No, in each culture.
    [Theory]
    [InlineData("es", "1.500,5", "15.000", "1.500,25", "15/03/2027", "Sí")]
    [InlineData("en", "1,500.5", "15,000", "1,500.25", "03/15/2027", "Yes")]
    public void Typed_values_in_a_catalog_title_use_the_screen_format_of_each_culture(string culture, string value, string wholeNumber,
        string exactNumber, string date, string yesNo)
    {
        var fields = new[] { Field("integer"), Field("decimal"), Field("date"), Field("boolean") };
        var stored = new Dictionary<string, object>
        {
            [fields[0].Id.ToString("D")] = 15000L,
            [fields[1].Id.ToString("D")] = "1500.25",
            [fields[2].Id.ToString("D")] = "2027-03-15",
            [fields[3].Id.ToString("D")] = true,
        };
        var row = new FormCatalogStoredItemRow(Guid.CreateVersion7(), Guid.CreateVersion7(), "A1", "Equipo", "Team", null, null, 0, true,
            "1500.5", JsonSerializer.Serialize(stored, JsonSerializerOptions.Web));

        var values = FormCatalogFieldPresentation.Values(row, fields, new Dictionary<Guid, FormCatalogStoredItemRow>(), culture, "Decimal");

        Assert.Equal(value, values["value"]);
        Assert.Equal([wholeNumber, exactNumber, date, yesNo], fields.Select(field => values[field.Id.ToString("D")]));
    }

    private static BusinessFormCatalogField Field(string type) => new(Guid.CreateVersion7(), type,
        new Dictionary<string, string> { ["es"] = type }, type, null, false, false, null, null, null, "data", 0, true);
}
