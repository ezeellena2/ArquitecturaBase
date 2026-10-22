using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormPublicationMultipleCatalogTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(20, false)]
    [InlineData(21, true)]
    public async Task A_manual_catalog_blocks_multiple_selection_only_when_more_than_twenty_items_are_active(int count, bool blocked)
    {
        var fixture = new FormPublicationMultipleCatalogFixture { ActiveItems = count };
        var field = fixture.Field();
        var issues = await fixture.Guard.ValidateCatalogsAsync([field], TestContext.Current.CancellationToken);
        if (blocked)
        {
            var issue = Assert.Single(issues);
            Assert.Equal("Forms.Publication.CatalogTooLargeForMultiple", issue.Code);
            Assert.Equal(field.FieldId, issue.FieldId);
            Assert.Equal("draft.fields[0].source.catalog", issue.Path);
        }
        else Assert.Empty(issues);
        Assert.Equal([fixture.CatalogId], fixture.CatalogReads);
        Assert.Equal([fixture.CatalogId], fixture.CountReads);
    }

    [Fact]
    public async Task A_shared_catalog_is_counted_once_and_each_multiple_question_keeps_its_own_issue_path()
    {
        var fixture = new FormPublicationMultipleCatalogFixture { ActiveItems = 21 };
        var first = fixture.Field(); var second = fixture.Field();
        var issues = await fixture.Guard.ValidateCatalogsAsync([fixture.Field("choice"), first, second], TestContext.Current.CancellationToken);
        Assert.Equal([first.FieldId, second.FieldId], issues.Select(issue => issue.FieldId));
        Assert.Equal(["draft.fields[1].source.catalog", "draft.fields[2].source.catalog"], issues.Select(issue => issue.Path));
        Assert.All(issues, issue => Assert.Equal("Forms.Publication.CatalogTooLargeForMultiple", issue.Code));
        Assert.Equal([fixture.CatalogId], fixture.CatalogReads);
        Assert.Equal([fixture.CatalogId], fixture.CountReads);
    }

    [Theory]
    [InlineData("Manual", "choice")]
    [InlineData("Filters", "choice")]
    [InlineData("Filters", "multiChoice")]
    public async Task Ordinary_choices_and_dynamic_filters_do_not_use_the_manual_multiple_count(string kind, string type)
    {
        var fixture = new FormPublicationMultipleCatalogFixture { Kind = kind, ActiveItems = 999 };
        Assert.Empty(await fixture.Guard.ValidateCatalogsAsync([fixture.Field(type)], TestContext.Current.CancellationToken));
        Assert.Empty(fixture.CountReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unavailable_catalog_keeps_the_existing_error_without_reading_its_item_count(bool missing)
    {
        var fixture = new FormPublicationMultipleCatalogFixture { IsActive = false, Missing = missing, ActiveItems = 21 };
        var issue = Assert.Single(await fixture.Guard.ValidateCatalogsAsync([fixture.Field()], TestContext.Current.CancellationToken));
        Assert.Equal("Forms.Publication.CatalogInvalid", issue.Code);
        Assert.Empty(fixture.CountReads);
    }
}
