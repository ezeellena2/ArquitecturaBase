using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogImportOptionalColumnsTests
{
    private static readonly Guid CatalogId = Guid.Parse("01990000-0000-7000-8000-000000000081");

    [Theory]
    [InlineData(FormCatalogImportMode.Append)]
    [InlineData(FormCatalogImportMode.Replace)]
    public void Missing_optional_columns_preserve_existing_translations_and_details(FormCatalogImportMode mode)
    {
        var existing = Item(active: false);
        var plan = Plan(existing, ["Code", "Text"], ["A", "Changed"], mode);
        var mutation = Assert.Single(plan.Items);

        Assert.Equal(existing.Id, mutation.ItemId);
        Assert.Equal("Changed", mutation.Text!["es"]);
        Assert.Equal("English", mutation.Text["en"]);
        Assert.Equal("Original detail", mutation.Detail!["es"]);
        Assert.Equal("English detail", mutation.Detail["en"]);
        Assert.True(mutation.IsActive);
        Assert.Equal(new[] { "text", "active" }, Assert.Single(plan.Preview.Changed).Changes.Select(change => change.Column));
    }

    [Theory]
    [InlineData("English text", "textEn", false)]
    [InlineData("Detail", "detail", false)]
    [InlineData("English detail", "detailEn", false)]
    [InlineData("English text", "textEn", true)]
    [InlineData("Detail", "detail", true)]
    [InlineData("English detail", "detailEn", true)]
    public void A_present_empty_optional_column_clears_only_its_own_value(string header, string key, bool shortRow)
    {
        var plan = Plan(Item(), ["Code", "Text", header], shortRow ? ["A", "Original"] : ["A", "Original", ""]);
        var mutation = Assert.Single(plan.Items);

        Assert.Equal(key == "textEn" ? null : "English", mutation.Text!["en"]);
        Assert.Equal(key == "detail" ? null : "Original detail", mutation.Detail!["es"]);
        Assert.Equal(key == "detailEn" ? null : "English detail", mutation.Detail["en"]);
        var change = Assert.Single(Assert.Single(plan.Preview.Changed).Changes);
        Assert.Equal(key, change.Column);
        Assert.Null(change.After);
    }

    [Fact]
    public void A_new_option_without_optional_columns_still_has_empty_translations_and_details()
    {
        var plan = Plan(Item(), ["Code", "Text"], ["B", "New"], FormCatalogImportMode.Append);
        var mutation = Assert.Single(plan.Items);

        Assert.Null(mutation.ItemId);
        Assert.Null(mutation.Text!["en"]);
        Assert.Null(mutation.Detail!["es"]);
        Assert.Null(mutation.Detail["en"]);
    }

    [Fact]
    public void An_invalid_row_does_not_change_an_existing_option_with_missing_optional_columns()
    {
        var plan = Plan(Item(), ["Code", "Text"], ["A", new string('x', 31)]);

        Assert.Empty(plan.Items);
        Assert.Equal(1, plan.Preview.Counts.ErrorRows);
        Assert.Empty(plan.Preview.Changed);
        Assert.Empty(plan.Preview.Deactivated);
    }

    private static FormCatalogStoredItemRow Item(bool active = true) =>
        new(Guid.NewGuid(), CatalogId, "A", "Original", "English", "Original detail", "English detail", 7, active, null);

    private static FormCatalogImportPlan Plan(FormCatalogStoredItemRow item, string[] headers, string[] row,
        FormCatalogImportMode mode = FormCatalogImportMode.Replace)
    {
        var sheet = new FormSpreadsheet("Options", new string('a', 64),
            [new(1, headers.Select(text => new FormSpreadsheetCell(text)).ToArray()),
             new(2, row.Select(text => new FormSpreadsheetCell(text)).ToArray())]);
        var result = FormCatalogImportPolicy.Build(new(CatalogId, 1, BusinessFormCatalogValueType.Text, [item]), sheet, mode, "en");
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
