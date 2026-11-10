using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using P = ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormsPermissions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// CP2-A: «Carpetas…» saves the whole set of folders of a form or a catalog against the one it read, with the permission of that
// item, in one transaction under the folders lock.
public sealed class FormFolderItemServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_whole_set_replaces_the_one_that_was_read_in_one_transaction()
    {
        using var fixture = new FormFolderFixture(P.Read, P.Manage);
        var billing = fixture.Arrange("Facturación");
        var deliveries = fixture.Arrange("Repartos");
        var archive = fixture.Arrange("Archivo");
        var form = Guid.NewGuid();
        fixture.File(billing, FormFolderItemKind.Form, form);
        fixture.File(deliveries, FormFolderItemKind.Form, form);

        Assert.Equal(new[] { billing.Id, deliveries.Id }.Order(), (await fixture.ItemFolders.GetAsync(FormFolderItemKind.Form, form, Ct)).Value.FolderIds.Order());

        var saved = await fixture.ItemFolders.SetAsync(FormFolderItemKind.Form, form, new([deliveries.Id, billing.Id], [deliveries.Id, archive.Id]), Ct);

        Assert.True(saved.IsSuccess);
        Assert.Equal(new[] { deliveries.Id, archive.Id }.Order(), saved.Value.FolderIds.Order());
        Assert.Equal(new[] { deliveries.Id, archive.Id }.Order(), fixture.Memberships.Select(value => value.FolderId).Order());
        Assert.Equal((1, 1, 1), (fixture.UnitOfWork.Transactions, fixture.UnitOfWork.Commits, fixture.Locks));
    }

    [Fact]
    public async Task A_set_that_changed_since_it_was_read_is_a_conflict_and_changes_nothing()
    {
        using var fixture = new FormFolderFixture(P.CatalogsRead, P.CatalogsManage);
        var billing = fixture.Arrange("Facturación");
        var deliveries = fixture.Arrange("Repartos");
        var catalog = Guid.NewGuid();
        fixture.File(billing, FormFolderItemKind.Catalog, catalog);

        var stale = await fixture.ItemFolders.SetAsync(FormFolderItemKind.Catalog, catalog, new([], [deliveries.Id]), Ct);

        Assert.Equal(FormFolderErrors.SetChanged, stale.Error);
        Assert.Equal([billing.Id], fixture.Memberships.Select(value => value.FolderId));
        Assert.Equal(0, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Leaving_no_folder_takes_the_item_out_of_all_and_a_missing_folder_is_not_found()
    {
        using var fixture = new FormFolderFixture(P.CatalogsRead, P.CatalogsManage);
        var billing = fixture.Arrange("Facturación");
        var catalog = Guid.NewGuid();
        fixture.File(billing, FormFolderItemKind.Catalog, catalog);

        Assert.Equal(FormFolderErrors.NotFound, (await fixture.ItemFolders.SetAsync(FormFolderItemKind.Catalog, catalog,
            new([billing.Id], [billing.Id, Guid.NewGuid()]), Ct)).Error);
        Assert.True((await fixture.ItemFolders.SetAsync(FormFolderItemKind.Catalog, catalog, new([billing.Id], []), Ct)).IsSuccess);

        Assert.Empty(fixture.Memberships);
    }

    [Fact]
    public async Task Each_item_needs_the_permission_of_its_own_kind_and_must_exist()
    {
        using var fixture = new FormFolderFixture(P.Read, P.CatalogsRead, P.CatalogsManage);
        var form = Guid.NewGuid();
        var catalog = Guid.NewGuid();
        fixture.Items.Add((FormFolderItemKind.Form, form));
        fixture.Items.Add((FormFolderItemKind.Catalog, catalog));

        Assert.True((await fixture.ItemFolders.GetAsync(FormFolderItemKind.Form, form, Ct)).IsSuccess);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.ItemFolders.SetAsync(FormFolderItemKind.Form, form, new([], []), Ct)).Error);
        Assert.True((await fixture.ItemFolders.SetAsync(FormFolderItemKind.Catalog, catalog, new([], []), Ct)).IsSuccess);
        Assert.Equal(FormCatalogErrors.NotFound, (await fixture.ItemFolders.GetAsync(FormFolderItemKind.Catalog, Guid.NewGuid(), Ct)).Error);
        Assert.Equal(FormCatalogErrors.NotFound, (await fixture.ItemFolders.SetAsync(FormFolderItemKind.Catalog, Guid.NewGuid(), new([], []), Ct)).Error);

        fixture.Permissions.Permissions[fixture.Actor] = [P.Manage, P.CatalogsManage];
        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.ItemFolders.GetAsync(FormFolderItemKind.Form, form, Ct)).Error);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.ItemFolders.GetAsync(FormFolderItemKind.Catalog, catalog, Ct)).Error);
        fixture.Permissions.Permissions[fixture.Actor] = [P.Read, P.Manage];
        Assert.Equal(FormDefinitionErrors.NotFound, (await fixture.ItemFolders.GetAsync(FormFolderItemKind.Form, Guid.NewGuid(), Ct)).Error);
    }

    [Fact]
    public async Task A_repeated_empty_or_missing_list_is_invalid_and_opens_no_transaction()
    {
        using var fixture = new FormFolderFixture(P.Read, P.Manage);
        var form = Guid.NewGuid();
        var folder = Guid.NewGuid();
        fixture.Items.Add((FormFolderItemKind.Form, form));

        foreach (var request in new ArquitecturaBase.Application.Modules.BusinessForms.Models.SetFormItemFoldersRequest[]
            { new(null, []), new([], null), new([], [folder, folder]), new([Guid.Empty], []), new([], Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray()) })
            Assert.IsType<ValidationError>((await fixture.ItemFolders.SetAsync(FormFolderItemKind.Form, form, request, Ct)).Error);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }
}
