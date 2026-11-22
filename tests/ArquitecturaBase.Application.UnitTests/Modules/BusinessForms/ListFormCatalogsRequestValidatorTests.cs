using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class ListFormCatalogsRequestValidatorTests
{
    // Every column of «Catálogos» with a datum sorts (OT1-C), in both directions, by the id of its column in the front; «De dónde
    // salen» keeps the name of the backend, kind.
    [Theory]
    [InlineData("name")]
    [InlineData("kind")]
    [InlineData("-kind")]
    [InlineData("optionsCount")]
    [InlineData("-usageCount")]
    [InlineData("state")]
    [InlineData("-state")]
    public void Every_column_of_the_list_can_be_sorted(string sort) =>
        Assert.True(new ListFormCatalogsRequestValidator().Validate(new ListFormCatalogsRequest { Sort = sort }).IsValid);

    [Theory]
    [InlineData("origin")]
    [InlineData("actions")]
    [InlineData("description")]
    public void Other_fields_cannot_be_sorted(string sort) =>
        Assert.False(new ListFormCatalogsRequestValidator().Validate(new ListFormCatalogsRequest { Sort = sort }).IsValid);
}
