using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogAlphabeticalOrderTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Alphabetical_save_orders_the_complete_catalog_by_its_original_text_and_preserves_values(string culture)
    {
        using var scope = new CultureScope(culture);
        var catalog = Catalog();
        var zulu = catalog.AddManualItem("Z", "Zulú", "A", null, null, 1, "0.5").Value;
        var alamo = catalog.AddManualItem("A", "Álamo", "Z", null, null, 501, "1.5").Value;
        var inactive = catalog.AddManualItem("B", "Bajo", "Y", null, null, 1000, "2.5").Value;
        Assert.True(catalog.SetManualItemActive(inactive, false).IsSuccess);
        var repository = new FormCatalogCapacityRepository(catalog, [zulu, alamo, inactive]);
        var work = new FakeUnitOfWork();
        var result = await Service(repository, work).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [new("new", null, "C", Texts("Campo"), null, true, "3.5")], SortAlphabetically: true), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "A", "B", "C", "Z" }, repository.Items.OrderBy(item => item.Position).Select(item => item.Code));
        Assert.Equal(new[] { 1, 2, 3, 4 }, repository.Items.OrderBy(item => item.Position).Select(item => item.Position));
        Assert.Equal("0.5", zulu.Value);
        Assert.Equal("1.5", alamo.Value);
        Assert.Equal("2.5", inactive.Value);
        Assert.False(inactive.IsActive);
        Assert.Equal("3.5", repository.Items.Single(item => item.Code == "C").Value);
        Assert.Equal(2, catalog.Revision);
        Assert.Equal(1, work.Commits);
    }

    [Fact]
    public async Task Equal_original_texts_keep_the_existing_position_and_id_tiebreaker()
    {
        var catalog = Catalog();
        var later = catalog.AddManualItem("B", "Igual", null, null, null, 8, "1").Value;
        var earlier = catalog.AddManualItem("A", "Igual", null, null, null, 3, "2").Value;
        var repository = new FormCatalogCapacityRepository(catalog, [later, earlier]);
        var work = new FakeUnitOfWork();
        var result = await Service(repository, work).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], Moves: [], SortAlphabetically: true), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, earlier.Position);
        Assert.Equal(2, later.Position);
    }

    [Fact]
    public async Task Alphabetical_save_cannot_be_combined_with_explicit_moves()
    {
        var catalog = Catalog();
        var first = catalog.AddManualItem("B", "B", null, null, null, 1, "1").Value;
        var second = catalog.AddManualItem("A", "A", null, null, null, 2, "2").Value;
        var repository = new FormCatalogCapacityRepository(catalog, [first, second]);
        var work = new FakeUnitOfWork();
        var result = await Service(repository, work).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], [new(first.Id, "down")], SortAlphabetically: true), Ct);
        Assert.Contains("moves", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        Assert.Equal(0, work.Transactions);
        Assert.Empty(repository.Locks);
        Assert.Equal(1, catalog.Revision);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static Dictionary<string, string?> Texts(string es = "Propio") => new() { ["es"] = es, ["en"] = "Own" };
    private static BusinessFormCatalog Catalog() => BusinessFormCatalog.CreateManual("es", 
        new Dictionary<string,string> { ["es"] = "Propio", ["en"] = "Own" },
        new Dictionary<string,string> { ["es"] = "Propio", ["en"] = "Own" }, BusinessFormCatalogValueType.Decimal);
    private static FormCatalogService Service(FormCatalogCapacityRepository repository, FakeUnitOfWork work)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        return new(repository, null!, work, RequestValidators.For(new UpdateManualCatalogRequestValidator()),
            new(new FakeCurrentUser { UserId = actor }, permissions), null!, new(repository), new(null!));
    }
}
