using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogCapacityTests
{
    [Theory]
    [InlineData(true, 501)]
    [InlineData(true, 10000)]
    [InlineData(false, 501)]
    [InlineData(false, 10000)]
    public async Task Saving_manual_items_accepts_the_whole_catalog_capacity(bool creating, int count)
    {
        var catalog = Catalog();
        var repository = new FormCatalogCapacityRepository(creating ? null : catalog);
        var work = new FakeUnitOfWork();
        var service = Service(repository, work);
        var items = Enumerable.Range(1, count).Select(index => Input(index)).ToArray();

        var result = creating
            ? await service.CreateManualAsync(new(Texts(), Texts(), items), Ct)
            : await service.UpdateManualAsync(catalog.Id, new(1, Texts(), Texts(), items), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(count, result.Value.Catalog.ItemsCount);
        Assert.Equal(count, result.Value.ItemIdentityMap.Count);
        Assert.Equal(count, repository.Items.Count);
        Assert.Equal(count, repository.Items.Select(item => item.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, count), repository.Items.Select(item => item.Position));
        Assert.Equal(creating ? 1L : 2L, result.Value.Catalog.Revision);
        Assert.Equal(1, work.Transactions);
        Assert.Equal(1, work.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, work.LastPolicy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task More_than_ten_thousand_input_rows_are_rejected_before_writing(bool creating)
    {
        var catalog = Catalog();
        var repository = new FormCatalogCapacityRepository(creating ? null : catalog);
        var work = new FakeUnitOfWork();
        var service = Service(repository, work);
        var items = Enumerable.Range(1, 10001).Select(index => Input(index)).ToArray();

        var result = creating
            ? await service.CreateManualAsync(new(Texts(), Texts(), items), Ct)
            : await service.UpdateManualAsync(catalog.Id, new(1, Texts(), Texts(), items), Ct);

        Assert.True(result.IsFailure);
        Assert.Contains("items", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        Assert.Empty(repository.Items);
        Assert.Empty(repository.Locks);
        Assert.Equal(0, work.Transactions);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(9999)]
    public async Task Adding_one_row_counts_existing_rows_against_the_catalog_capacity(int existingCount)
    {
        var catalog = Catalog();
        var repository = new FormCatalogCapacityRepository(catalog, Stored(catalog, existingCount));
        var work = new FakeUnitOfWork();
        var service = Service(repository, work);

        var result = await service.UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [Input(existingCount + 1)]), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(existingCount + 1, repository.Items.Count);
        Assert.Equal(existingCount + 1, result.Value.Catalog.ItemsCount);
        Assert.Single(result.Value.ItemIdentityMap);
        Assert.Equal(existingCount + 1, repository.Items[^1].Position);
        Assert.Equal(2L, result.Value.Catalog.Revision);
        Assert.Equal(1, work.Commits);
    }

    [Fact]
    public async Task A_small_update_cannot_grow_a_full_catalog_past_ten_thousand_rows()
    {
        var catalog = Catalog();
        var repository = new FormCatalogCapacityRepository(catalog, Stored(catalog, 10000));
        var work = new FakeUnitOfWork();
        var service = Service(repository, work);

        var result = await service.UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [Input(10001)]), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal("Forms.Catalog.TooManyItems", result.Error.Code);
        Assert.Contains("items", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        Assert.Equal(10000, repository.Items.Count);
        Assert.Equal(0, work.Commits);
        Assert.Equal(1, work.Rollbacks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_duplicate_code_on_update_points_to_its_input_row(bool conflictsWithUnchangedRow)
    {
        var catalog = Catalog();
        var repository = new FormCatalogCapacityRepository(catalog,
            Stored(catalog, conflictsWithUnchangedRow ? 3 : 2));
        var work = new FakeUnitOfWork();
        var service = Service(repository, work);
        var first = Input(1, repository.Items[0].Id) with { Code = "CHANGED" };
        var second = Input(2, repository.Items[1].Id) with
        {
            Code = conflictsWithUnchangedRow ? "CODE-3" : "changed"
        };

        var result = await service.UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [first, second]), Ct);

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(FormCatalogErrors.DuplicateItemCode.Code, error.Code);
        Assert.Equal("items[1].code", Assert.Single(error.Errors).Key);
        Assert.Equal(0, work.Commits);
        Assert.Equal(1, work.Rollbacks);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public async Task Increasing_catalog_capacity_keeps_the_five_hundred_move_limit(int moveCount, bool allowed)
    {
        var catalog = Catalog();
        var repository = new FormCatalogCapacityRepository(catalog, Stored(catalog, 2));
        var firstId = repository.Items[0].Id;
        var work = new FakeUnitOfWork();
        var service = Service(repository, work);
        var moves = Enumerable.Range(0, moveCount)
            .Select(index => new FormCatalogManualItemMoveRequest(firstId, index % 2 == 0 ? "down" : "up"))
            .ToArray();

        var result = await service.UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], moves), Ct);

        Assert.Equal(allowed, result.IsSuccess);
        if (allowed)
        {
            Assert.Equal(new[] { 1, 2 }, repository.Items.Select(item => item.Position));
            Assert.Equal(firstId, repository.Items[0].Id);
            Assert.Equal(2L, result.Value.Catalog.Revision);
            Assert.Equal(1, work.Commits);
        }
        else
        {
            Assert.Contains("moves", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
            Assert.Equal(0, work.Transactions);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BusinessFormCatalog Catalog() => BusinessFormCatalog.CreateManual("es", 
        new Dictionary<string, string> { ["es"] = "Opciones", ["en"] = "Options" },
        new Dictionary<string, string> { ["es"] = "Catálogo propio", ["en"] = "Own catalog" });

    private static Dictionary<string, string?> Texts() => new()
    {
        ["es"] = "Opciones", ["en"] = "Options"
    };

    private static FormCatalogManualItemSaveRequest Input(int index, Guid? itemId = null)
    {
        var number = index.ToString(CultureInfo.InvariantCulture);
        return new("row-" + number, itemId, "CODE-" + number,
            new Dictionary<string, string?> { ["es"] = "Opción " + number, ["en"] = null },
            null, index % 2 == 0);
    }

    private static List<BusinessFormCatalogItem> Stored(BusinessFormCatalog catalog, int count) =>
        Enumerable.Range(1, count).Select(index =>
        {
            var input = Input(index);
            return catalog.AddManualItem(input.Code!, input.Text!["es"]!, null, null, null, index).Value;
        }).ToList();

    private static FormCatalogService Service(FormCatalogCapacityRepository repository, FakeUnitOfWork work)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        return new(repository, null!, work,
            RequestValidators.For(new CreateManualCatalogRequestValidator(), new UpdateManualCatalogRequestValidator()),
            new(new FakeCurrentUser { UserId = actor }, permissions), null!, new(repository), new(null!));
    }
}
