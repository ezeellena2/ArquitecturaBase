using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogItemDeletionTests
{
    [Fact]
    public async Task An_unused_item_is_deleted_and_the_others_keep_consecutive_positions()
    {
        var (catalog, items) = Catalog("A", "B", "C");
        var repository = new FormCatalogCapacityRepository(catalog, items);
        var reader = new FormCatalogFieldReader();
        var work = new FakeUnitOfWork();

        var result = await Service(repository, reader, work).UpdateManualAsync(catalog.Id, new(1, Texts(), Texts(), [], DeleteItemIds: [items[1].Id]), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { ("A", 1), ("C", 2) }, repository.Items.OrderBy(item => item.Position).Select(item => (item.Code, item.Position)));
        Assert.Equal(2, result.Value.Catalog.ItemsCount);
        Assert.Equal(2, catalog.Revision);
        Assert.Equal(1, work.Commits);
        Assert.Equal([items[1].Id], reader.ItemUseReads.Single());
    }

    [Fact]
    public async Task An_item_in_use_is_not_deleted_and_the_conflict_names_its_uses()
    {
        var (catalog, items) = Catalog("A", "B", "C");
        var repository = new FormCatalogCapacityRepository(catalog, items);
        var reader = new FormCatalogFieldReader();
        reader.ItemUses.Add(new(items[2].Id, "records"));
        reader.ItemUses.Add(new(items[2].Id, "forms"));
        var work = new FakeUnitOfWork();

        var result = await Service(repository, reader, work).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], DeleteItemIds: [items[0].Id, items[2].Id]), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(FormCatalogErrors.ItemInUse.Code, error.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal(["deleteItemIds[1]"], error.Errors.Keys);
        Assert.Equal(new[] { "records", "forms" }, Assert.IsAssignableFrom<IEnumerable<string>>(error.Metadata!["uses"]));
        Assert.Equal(3, repository.Items.Count);
        Assert.Equal(0, work.Commits);
        Assert.Equal(1, catalog.Revision);
    }

    [Fact]
    public async Task An_item_of_another_catalog_cannot_be_deleted()
    {
        var (catalog, items) = Catalog("A");
        var repository = new FormCatalogCapacityRepository(catalog, items);
        var reader = new FormCatalogFieldReader();

        var result = await Service(repository, reader, new FakeUnitOfWork()).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], DeleteItemIds: [Guid.CreateVersion7()]), Ct);

        Assert.Equal(FormCatalogErrors.ItemNotFound, result.Error);
        Assert.Empty(reader.ItemUseReads);
        Assert.Single(repository.Items);
    }

    [Fact]
    public async Task A_deleted_item_cannot_be_edited_or_moved_in_the_same_save()
    {
        var (catalog, items) = Catalog("A", "B");
        var repository = new FormCatalogCapacityRepository(catalog, items);
        var edited = await Service(repository, new FormCatalogFieldReader(), new FakeUnitOfWork()).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [new("a", items[0].Id, "A", Texts("Otra"), null, true, null)], DeleteItemIds: [items[0].Id]), Ct);
        var moved = await Service(repository, new FormCatalogFieldReader(), new FakeUnitOfWork()).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], [new(items[1].Id, "up")], DeleteItemIds: [items[1].Id]), Ct);

        Assert.Equal(["deleteItemIds[0]"], Assert.IsType<ValidationError>(edited.Error).Errors.Keys);
        Assert.Equal(["deleteItemIds[0]"], Assert.IsType<ValidationError>(moved.Error).Errors.Keys);
        Assert.Equal(2, repository.Items.Count);
    }

    [Fact]
    public async Task Deleting_an_item_frees_its_code_for_a_new_option_in_the_same_save()
    {
        var (catalog, items) = Catalog("A", "B");
        var repository = new FormCatalogCapacityRepository(catalog, items);

        var result = await Service(repository, new FormCatalogFieldReader(), new FakeUnitOfWork()).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [new("new", null, "B", Texts("Nueva"), null, true, null)], DeleteItemIds: [items[1].Id]), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { ("A", 1, "A"), ("B", 2, "Nueva") }, repository.Items.OrderBy(item => item.Position).Select(item => (item.Code, item.Position, item.TextEs ?? "")));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void The_validator_rejects_repeated_or_empty_identities_to_delete(bool repeated, bool empty)
    {
        var id = Guid.CreateVersion7();
        IReadOnlyList<Guid> ids = repeated ? [id, id] : empty ? [Guid.Empty] : [id];
        var result = new UpdateManualCatalogRequestValidator().Validate(new UpdateManualCatalogRequest(1, Texts(), Texts(), [], DeleteItemIds: ids));
        Assert.Contains(result.Errors, failure => failure.PropertyName == "deleteItemIds");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static Dictionary<string, string?> Texts(string es = "Propio") => new() { ["es"] = es, ["en"] = null };

    private static (BusinessFormCatalog Catalog, BusinessFormCatalogItem[] Items) Catalog(params string[] codes)
    {
        var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Propio" }, new Dictionary<string, string>());
        return (catalog, codes.Select((code, index) => catalog.AddManualItem(code, code, null, null, null, index + 1).Value).ToArray());
    }

    private static FormCatalogService Service(FormCatalogCapacityRepository repository, FormCatalogFieldReader reader, FakeUnitOfWork work)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        return new(repository, reader, work, RequestValidators.For(new UpdateManualCatalogRequestValidator()),
            new(new FakeCurrentUser { UserId = actor }, permissions), null!, new(repository), new(reader));
    }
}
