using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class ListFormDefinitionsRequestValidatorTests
{
    // Every column of «Diseñar formularios» with a datum sorts (OT1-C), in both directions, by the id of its column in the front.
    [Theory]
    [InlineData("name")]
    [InlineData("-questionCount")]
    [InlineData("displayState")]
    [InlineData("-displayState")]
    [InlineData("completedIn")]
    [InlineData("-completedIn")]
    [InlineData("activeVersion")]
    [InlineData("-activeVersion")]
    [InlineData("responseCount")]
    [InlineData("-responseCount")]
    [InlineData("-lastChangedAtUtc")]
    [InlineData("lastChangedBy")]
    [InlineData("-lastChangedBy")]
    public void Every_column_of_the_list_can_be_sorted(string sort) =>
        Assert.True(new ListFormDefinitionsRequestValidator().Validate(new ListFormDefinitionsRequest { Sort = sort }).IsValid);

    // «Deprecación», the withdrawal from the channel, comes from the module of the channel and not from SQL; the actions are no datum.
    [Theory]
    [InlineData("deprecation")]
    [InlineData("actions")]
    public void Other_columns_cannot_be_sorted(string sort) =>
        Assert.False(new ListFormDefinitionsRequestValidator().Validate(new ListFormDefinitionsRequest { Sort = sort }).IsValid);
}
