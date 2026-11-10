using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using P = ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormsPermissions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// CP1-A, CP3-A and CP5-A: the tree of each listing pruned for whoever does not administer folders, and every change in one
// transaction under the folders lock, with the revision, the levels, the cycles and the names checked there.
public sealed class FormFolderServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(P.Manage)]
    [InlineData(P.CatalogsManage)]
    public async Task A_folder_is_created_under_the_folders_lock_in_one_transaction(string permission)
    {
        using var fixture = new FormFolderFixture(permission);
        var parent = fixture.Arrange("Facturación");

        var result = await fixture.Folder.CreateAsync(new(parent.Id, "  Notas de crédito "), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal((parent.Id, "Notas de crédito", 1L, "None", (string?)null),
            (result.Value.ParentId, result.Value.Name, result.Value.Revision, result.Value.MenuPlacement, result.Value.MenuIcon));
        Assert.Equal((1, 1, 1), (fixture.UnitOfWork.Transactions, fixture.UnitOfWork.Commits, fixture.Locks));
        Assert.Contains(fixture.Folders, folder => folder.Id == result.Value.Id);
    }

    [Fact]
    public async Task Changing_folders_needs_to_manage_forms_or_catalogs_and_opens_no_transaction_otherwise()
    {
        using var fixture = new FormFolderFixture(P.Read, P.CatalogsRead, P.RecordsRead);
        var folder = fixture.Arrange("Facturación");

        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.Folder.CreateAsync(new(null, "Repartos"), Ct)).Error);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.Folder.UpdateAsync(folder.Id, new(1, "Facturas", null), Ct)).Error);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.Folder.DeleteAsync(folder.Id, new(1), Ct)).Error);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.Folder.SetMenuAsync(folder.Id, new(1, "Own", null), Ct)).Error);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.Folder.GetDeletionAsync(folder.Id, Ct)).Error);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task A_repeated_name_goes_on_the_name_and_a_sixth_level_on_the_parent()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var top = fixture.Arrange("Facturación");
        var deepest = top;
        for (var level = 2; level <= 5; level++) deepest = fixture.Arrange("Nivel " + level, deepest);

        var repeated = Assert.IsType<ValidationError>((await fixture.Folder.CreateAsync(new(null, "FACTURACIÓN"), Ct)).Error);
        var tooDeep = Assert.IsType<ValidationError>((await fixture.Folder.CreateAsync(new(deepest.Id, "Sexto"), Ct)).Error);
        var empty = Assert.IsType<ValidationError>((await fixture.Folder.CreateAsync(new(null, " "), Ct)).Error);

        Assert.Equal((FormFolderErrors.NameTaken.Code, true), (repeated.Code, repeated.Errors.ContainsKey("name")));
        Assert.Equal((FormFolderErrors.TooDeep.Code, true), (tooDeep.Code, tooDeep.Errors.ContainsKey("parentId")));
        Assert.True(empty.Errors.ContainsKey("name"));
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Equal(6 - 1, fixture.Folders.Count);
    }

    [Fact]
    public async Task A_missing_parent_is_not_found()
    {
        using var fixture = new FormFolderFixture(P.Manage);

        Assert.Equal(FormFolderErrors.NotFound, (await fixture.Folder.CreateAsync(new(Guid.NewGuid(), "Repartos"), Ct)).Error);
    }

    [Fact]
    public async Task Renaming_and_moving_check_the_revision_and_the_cycle_inside_the_transaction()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var top = fixture.Arrange("Facturación");
        var child = fixture.Arrange("Notas", top);
        var other = fixture.Arrange("Repartos");

        var cycle = Assert.IsType<ValidationError>((await fixture.Folder.UpdateAsync(top.Id, new(1, "Facturación", child.Id), Ct)).Error);
        Assert.Equal((FormFolderErrors.Cycle.Code, true), (cycle.Code, cycle.Errors.ContainsKey("parentId")));
        Assert.Equal(FormFolderErrors.RevisionConflict, (await fixture.Folder.UpdateAsync(top.Id, new(2, "Facturas", null), Ct)).Error);
        Assert.Equal(FormFolderErrors.NotFound, (await fixture.Folder.UpdateAsync(Guid.NewGuid(), new(1, "Facturas", null), Ct)).Error);

        var moved = await fixture.Folder.UpdateAsync(child.Id, new(1, "Notas de crédito", other.Id), Ct);

        Assert.True(moved.IsSuccess);
        Assert.Equal((other.Id, "Notas de crédito", 2L), (moved.Value.ParentId, moved.Value.Name, moved.Value.Revision));
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(fixture.UnitOfWork.Transactions, fixture.Locks);
    }

    [Fact]
    public async Task Deleting_a_folder_takes_its_subfolders_and_leaves_its_items_in_their_other_folders()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var top = fixture.Arrange("Facturación");
        var child = fixture.Arrange("Notas", top);
        var grandchild = fixture.Arrange("Viejas", child);
        var other = fixture.Arrange("Repartos");
        var form = Guid.NewGuid();
        var catalog = Guid.NewGuid();
        fixture.File(top, FormFolderItemKind.Form, form);
        fixture.File(grandchild, FormFolderItemKind.Form, form);
        fixture.File(child, FormFolderItemKind.Catalog, catalog);
        fixture.File(other, FormFolderItemKind.Catalog, catalog);

        var preview = await fixture.Folder.GetDeletionAsync(top.Id, Ct);
        Assert.Equal(new FormFolderDeletionResponse(1, 2, 1, 1), preview.Value);
        Assert.Equal(FormFolderErrors.RevisionConflict, (await fixture.Folder.DeleteAsync(top.Id, new(2), Ct)).Error);

        Assert.True((await fixture.Folder.DeleteAsync(top.Id, new(1), Ct)).IsSuccess);

        Assert.Equal([other.Id], fixture.Folders.Select(folder => folder.Id));
        Assert.Equal([(other.Id, catalog)], fixture.Memberships.Select(value => (value.FolderId, value.ItemId)));
        Assert.Equal(FormFolderErrors.NotFound, (await fixture.Folder.DeleteAsync(top.Id, new(1), Ct)).Error);
        Assert.Equal(FormFolderErrors.NotFound, (await fixture.Folder.GetDeletionAsync(top.Id, Ct)).Error);
    }

    [Fact]
    public async Task The_tree_shows_folders_with_something_visible_and_their_parents_and_everything_to_whoever_administers()
    {
        using var fixture = new FormFolderFixture(P.Read);
        var billing = fixture.Arrange("facturación");
        var notes = fixture.Arrange("Notas", billing);
        var old = fixture.Arrange("Viejas", notes);
        var empty = fixture.Arrange("Vacía", billing);
        var abc = fixture.Arrange("Abastecimiento");
        var design = Guid.NewGuid();
        var hidden = Guid.NewGuid();
        fixture.File(old, FormFolderItemKind.Form, design);
        fixture.File(abc, FormFolderItemKind.Form, hidden);
        fixture.File(empty, FormFolderItemKind.Catalog, Guid.NewGuid());
        fixture.Visible[FormFolderListing.Designs].Add(design);

        var reader = (await fixture.Folder.ListAsync(new("designs"), Ct)).Value;
        Assert.False(reader.CanManage);
        Assert.Equal(["facturación", "Notas", "Viejas"], reader.Folders.Select(folder => folder.Name));
        Assert.Equal(notes.Id, reader.Folders.Single(folder => folder.Name == "Viejas").ParentId);

        fixture.Permissions.Permissions[fixture.Actor] = [P.Read, P.Manage];
        var manager = (await fixture.Folder.ListAsync(new("designs"), Ct)).Value;
        Assert.True(manager.CanManage);
        Assert.Equal(["Abastecimiento", "facturación", "Notas", "Vacía", "Viejas"], manager.Folders.Select(folder => folder.Name));
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }

    [Theory]
    [InlineData("designs", P.CatalogsRead)]
    [InlineData("catalogs", P.Read)]
    [InlineData("forms", P.Read)]
    public async Task Each_listing_needs_the_permission_of_that_listing(string kind, string other)
    {
        using var fixture = new FormFolderFixture(other, P.CatalogsManage, P.Manage);

        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.Folder.ListAsync(new(kind), Ct)).Error);
    }

    [Theory]
    [InlineData(P.RecordsRead)]
    [InlineData(P.ApprovalsDecide)]
    [InlineData(P.RecordsCreate)]
    public async Task The_forms_tree_is_read_by_whoever_sees_the_forms_listing(string permission)
    {
        using var fixture = new FormFolderFixture(permission);
        var folder = fixture.Arrange("Repartos");
        var form = Guid.NewGuid();
        fixture.File(folder, FormFolderItemKind.Form, form);
        fixture.Visible[FormFolderListing.Forms].Add(form);

        Assert.Equal([folder.Id], (await fixture.Folder.ListAsync(new("forms"), Ct)).Value.Folders.Select(value => value.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("menu")]
    [InlineData("Designs")]
    public async Task An_unknown_listing_is_invalid(string? kind)
    {
        using var fixture = new FormFolderFixture(P.Read, P.CatalogsRead, P.RecordsRead);

        Assert.True(Assert.IsType<ValidationError>((await fixture.Folder.ListAsync(new(kind), Ct)).Error).Errors.ContainsKey("kind"));
    }

    [Fact]
    public async Task The_place_in_the_menu_keeps_its_icon_only_as_an_own_menu()
    {
        using var fixture = new FormFolderFixture(P.CatalogsManage);
        var folder = fixture.Arrange("Repartos");

        var own = await fixture.Folder.SetMenuAsync(folder.Id, new(1, "Own", null), Ct);
        Assert.Equal(("Own", "folder", 2L), (own.Value.MenuPlacement, own.Value.MenuIcon, own.Value.Revision));
        var icon = Assert.IsType<ValidationError>((await fixture.Folder.SetMenuAsync(folder.Id, new(2, "Forms", "truck"), Ct)).Error);
        Assert.Equal((FormFolderErrors.IconInvalid.Code, true), (icon.Code, icon.Errors.ContainsKey("icon")));
        Assert.True(Assert.IsType<ValidationError>((await fixture.Folder.SetMenuAsync(folder.Id, new(2, "Sidebar", null), Ct)).Error)
            .Errors.ContainsKey("placement"));
        Assert.Equal(FormFolderErrors.RevisionConflict, (await fixture.Folder.SetMenuAsync(folder.Id, new(1, "None", null), Ct)).Error);

        var panel = await fixture.Folder.SetMenuAsync(folder.Id, new(2, "Administration", null), Ct);
        Assert.Equal(("Administration", (string?)null, 3L), (panel.Value.MenuPlacement, panel.Value.MenuIcon, panel.Value.Revision));
        Assert.Equal(2, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_missing_revision_is_invalid_and_opens_no_transaction()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var folder = fixture.Arrange("Repartos");

        Assert.True(Assert.IsType<ValidationError>((await fixture.Folder.UpdateAsync(folder.Id, new(null, "Rutas", null), Ct)).Error)
            .Errors.ContainsKey("expectedRevision"));
        Assert.True(Assert.IsType<ValidationError>((await fixture.Folder.DeleteAsync(folder.Id, new(0), Ct)).Error)
            .Errors.ContainsKey("expectedRevision"));
        Assert.True(Assert.IsType<ValidationError>((await fixture.Folder.SetMenuAsync(folder.Id, new(null, "Own", null), Ct)).Error)
            .Errors.ContainsKey("expectedRevision"));
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }
}
