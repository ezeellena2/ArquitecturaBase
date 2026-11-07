using System.Globalization;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

/// <summary>
/// LD1-A (2026-11-07): a catalog question can ask first for the value of one of its catalog's fields. Publishing checks that the
/// field is still there, active and of a type that groups: text or an option of a catalog, with any role.
/// </summary>
public sealed class FormCatalogGroupByPublicationTests : IDisposable
{
    private readonly CultureInfo previousCulture = CultureInfo.CurrentUICulture;
    public FormCatalogGroupByPublicationTests() => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es");
    public void Dispose() => CultureInfo.CurrentUICulture = previousCulture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("text", "data")]
    [InlineData("text", "filter")]
    [InlineData("catalog", "data")]
    public async Task A_text_or_catalog_field_of_any_role_can_be_chosen_first(string type, string role)
    {
        var f = new FormPublicationMultipleCatalogFixture();
        var field = Field(type, role);
        f.Fields = [field];
        var question = f.Field("choice") with { Source = new(f.CatalogId, GroupBy: field.Id) };

        Assert.Empty(await f.Guard.ValidateCatalogsAsync([question], Ct));
        Assert.Empty((await f.Guard.InspectCatalogsAsync([question], Ct)).Issues);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("integer")]
    [InlineData("filters")]
    public async Task A_field_to_choose_first_that_is_gone_inactive_or_of_another_type_blocks_on_its_path(string problem)
    {
        var f = new FormPublicationMultipleCatalogFixture { Kind = problem == "filters" ? "Filters" : "Manual" };
        var field = Field(problem == "integer" ? "integer" : "text", "data") with { IsActive = problem != "inactive" };
        f.Fields = problem is "missing" or "filters" ? [] : [field];
        var question = f.Field("choice") with { Source = new(f.CatalogId, GroupBy: field.Id) };

        var inspected = await f.Guard.InspectCatalogsAsync([question], Ct);
        foreach (var issue in new[] { Assert.Single(inspected.Issues), Assert.Single(await f.Guard.ValidateCatalogsAsync([question], Ct)) })
        {
            Assert.Equal(FormPublicationErrors.GroupByInvalid.Code, issue.Code);
            Assert.Equal(question.FieldId, issue.FieldId);
            Assert.Equal("draft.fields[0].source.groupBy", issue.Path);
        }
        Assert.Empty(inspected.Warnings);
    }

    private static BusinessFormCatalogField Field(string type, string role) => new(Guid.CreateVersion7(), "group",
        new Dictionary<string, string> { ["es"] = "Rubro" }, type, type == "catalog" ? Guid.CreateVersion7() : null, false, false, null, null,
        type == "text" ? 300 : null, role, 0, true);
}
