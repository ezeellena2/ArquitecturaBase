using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using P = ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormsPermissions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// CP1-A, CP3-A and CM1 to CM6: the tree of each listing pruned for whoever does not see every folder, and every change in one
// transaction under the folders lock, with the revision, the two levels, the cycles, the names and the icon of a menu checked there.
public sealed partial class FormFolderServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_folder_is_created_under_the_folders_lock_in_one_transaction()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var parent = fixture.Arrange("Facturación");

        var result = await fixture.Folder.CreateAsync(new(parent.Id, "  Notas de crédito ", null), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal((parent.Id, "Notas de crédito", 1L, (string?)null),
            (result.Value.ParentId, result.Value.Name, result.Value.Revision, result.Value.Icon));
        Assert.Equal((1, 1, 1), (fixture.UnitOfWork.Transactions, fixture.UnitOfWork.Commits, fixture.Locks));
        Assert.Contains(fixture.Folders, folder => folder.Id == result.Value.Id);
    }

    [Fact]
    public async Task Changing_folders_needs_to_manage_forms_and_opens_no_transaction_otherwise()
    {
        using var fixture = new FormFolderFixture(P.Read, P.CatalogsRead, P.RecordsRead, P.CatalogsManage);
        var folder = fixture.Arrange("Facturación");

        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.Folder.CreateAsync(new(null, "Repartos", null), Ct)).Error);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.Folder.UpdateAsync(folder.Id, new(1, "Facturas", null, null), Ct)).Error);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.Folder.DeleteAsync(folder.Id, new(1), Ct)).Error);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.Folder.GetDeletionAsync(folder.Id, Ct)).Error);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.Folder.GetFormsAsync(folder.Id, Ct)).Error);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.Folder.SetFormsAsync(folder.Id, new([], []), Ct)).Error);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task A_menu_takes_the_icon_it_asks_for_or_the_first_free_one_and_a_subfolder_none()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        fixture.Arrange("Archivo", icon: "folder");
        fixture.Arrange("Camiones", icon: "truck");

        var first = await fixture.Folder.CreateAsync(new(null, "Compras", null), Ct);
        var chosen = await fixture.Folder.CreateAsync(new(null, "Campo", "leaf"), Ct);
        var next = await fixture.Folder.CreateAsync(new(null, "Facturación", null), Ct);
        var group = await fixture.Folder.CreateAsync(new(first.Value.Id, "Insumos", null), Ct);

        Assert.Equal(("package", "leaf", "receipt", (string?)null), (first.Value.Icon, chosen.Value.Icon, next.Value.Icon, group.Value.Icon));
        foreach (var (parentId, icon) in new[] { ((Guid?)null, "rocket"), (first.Value.Id, "truck") })
        {
            var invalid = Assert.IsType<ValidationError>((await fixture.Folder.CreateAsync(new(parentId, "Otra", icon), Ct)).Error);
            Assert.Equal((FormFolderErrors.IconInvalid.Code, true), (invalid.Code, invalid.Errors.ContainsKey("icon")));
        }
        Assert.Equal(4, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_repeated_name_goes_on_the_name_and_a_third_level_on_the_parent()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var top = fixture.Arrange("Facturación");
        var group = fixture.Arrange("Notas", top);

        var repeated = Assert.IsType<ValidationError>((await fixture.Folder.CreateAsync(new(null, "FACTURACIÓN", null), Ct)).Error);
        var tooDeep = Assert.IsType<ValidationError>((await fixture.Folder.CreateAsync(new(group.Id, "Viejas", null), Ct)).Error);
        var empty = Assert.IsType<ValidationError>((await fixture.Folder.CreateAsync(new(null, " ", null), Ct)).Error);

        Assert.Equal((FormFolderErrors.NameTaken.Code, true), (repeated.Code, repeated.Errors.ContainsKey("name")));
        Assert.Equal((FormFolderErrors.TooDeep.Code, true), (tooDeep.Code, tooDeep.Errors.ContainsKey("parentId")));
        Assert.True(empty.Errors.ContainsKey("name"));
        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Equal(2, fixture.Folders.Count);
    }

    [Fact]
    public async Task A_missing_parent_is_not_found()
    {
        using var fixture = new FormFolderFixture(P.Manage);

        Assert.Equal(FormFolderErrors.NotFound, (await fixture.Folder.CreateAsync(new(Guid.NewGuid(), "Repartos", null), Ct)).Error);
    }

    [Fact]
    public async Task Renaming_and_moving_check_the_revision_the_levels_and_the_cycle_inside_the_transaction()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var top = fixture.Arrange("Facturación");
        var child = fixture.Arrange("Notas", top);
        var other = fixture.Arrange("Repartos");

        var cycle = Assert.IsType<ValidationError>((await fixture.Folder.UpdateAsync(top.Id, new(1, "Facturación", child.Id, null), Ct)).Error);
        Assert.Equal((FormFolderErrors.Cycle.Code, true), (cycle.Code, cycle.Errors.ContainsKey("parentId")));
        var withChildren = Assert.IsType<ValidationError>((await fixture.Folder.UpdateAsync(top.Id, new(1, "Facturación", other.Id, null), Ct)).Error);
        Assert.Equal((FormFolderErrors.TooDeep.Code, true), (withChildren.Code, withChildren.Errors.ContainsKey("parentId")));
        Assert.Equal(FormFolderErrors.RevisionConflict, (await fixture.Folder.UpdateAsync(top.Id, new(2, "Facturas", null, null), Ct)).Error);
        Assert.Equal(FormFolderErrors.NotFound, (await fixture.Folder.UpdateAsync(Guid.NewGuid(), new(1, "Facturas", null, null), Ct)).Error);

        var moved = await fixture.Folder.UpdateAsync(child.Id, new(1, "Notas de crédito", other.Id, null), Ct);

        Assert.True(moved.IsSuccess);
        Assert.Equal((other.Id, "Notas de crédito", 2L), (moved.Value.ParentId, moved.Value.Name, moved.Value.Revision));
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(fixture.UnitOfWork.Transactions, fixture.Locks);
    }

    [Fact]
    public async Task A_folder_that_goes_up_takes_the_icon_asked_or_the_first_free_and_one_that_goes_down_loses_it()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var deliveries = fixture.Arrange("Repartos", icon: "folder");
        var north = fixture.Arrange("Norte", deliveries);
        var south = fixture.Arrange("Sur", deliveries);
        var billing = fixture.Arrange("Facturación", icon: "receipt");

        var up = await fixture.Folder.UpdateAsync(north.Id, new(1, "Norte", null, null), Ct);
        var chosen = await fixture.Folder.UpdateAsync(south.Id, new(1, "Sur", null, "leaf"), Ct);
        var down = await fixture.Folder.UpdateAsync(billing.Id, new(1, "Facturación", deliveries.Id, null), Ct);

        Assert.Equal(((Guid?)null, "truck", 2L), (up.Value.ParentId, up.Value.Icon, up.Value.Revision));
        Assert.Equal("leaf", chosen.Value.Icon);
        Assert.Equal((deliveries.Id, (string?)null), (down.Value.ParentId, down.Value.Icon));
        var invalid = Assert.IsType<ValidationError>((await fixture.Folder.UpdateAsync(north.Id, new(2, "Norte", deliveries.Id, "truck"), Ct)).Error);
        Assert.Equal((FormFolderErrors.IconInvalid.Code, true), (invalid.Code, invalid.Errors.ContainsKey("icon")));
        Assert.Equal(("truck", (Guid?)null, 2L), (north.MenuIcon, north.ParentId, north.Revision));
        Assert.Equal(3, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_menu_keeps_its_icon_when_none_comes_and_an_unchanged_save_keeps_the_revision()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var folder = fixture.Arrange("Repartos", icon: "truck");

        var unchanged = await fixture.Folder.UpdateAsync(folder.Id, new(1, "Repartos", null, null), Ct);
        var same = await fixture.Folder.UpdateAsync(folder.Id, new(1, "Repartos", null, "truck"), Ct);
        Assert.Equal(("truck", 1L), (unchanged.Value.Icon, unchanged.Value.Revision));
        Assert.Equal(1L, same.Value.Revision);

        var icon = await fixture.Folder.UpdateAsync(folder.Id, new(1, "Repartos", null, "package"), Ct);

        Assert.Equal(("package", 2L), (icon.Value.Icon, icon.Value.Revision));
        Assert.Equal(FormFolderErrors.RevisionConflict, (await fixture.Folder.UpdateAsync(folder.Id, new(1, "Repartos", null, "chart"), Ct)).Error);
    }

    // Until the second deployment of FoldersAsMenus the previous version may still write below the second level: the writes see the two
    // levels the tree shows, so a menu and a group with something hidden below change as any other and the hidden folder is not found.
    [Fact]
    public async Task A_menu_and_a_group_with_a_hidden_third_level_change_as_any_other_and_the_hidden_folder_is_not_found()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var menu = fixture.Arrange("Repartos", icon: "truck");
        var group = fixture.Arrange("Norte", menu);
        var hidden = fixture.Arrange("Nivel 3", group);

        var renamedMenu = await fixture.Folder.UpdateAsync(menu.Id, new(1, "Entregas", null, "package"), Ct);
        var renamedGroup = await fixture.Folder.UpdateAsync(group.Id, new(1, "Zona norte", menu.Id, null), Ct);

        Assert.Equal(("Entregas", "package", 2L), (renamedMenu.Value.Name, renamedMenu.Value.Icon, renamedMenu.Value.Revision));
        Assert.Equal(("Zona norte", (string?)null, 2L), (renamedGroup.Value.Name, renamedGroup.Value.Icon, renamedGroup.Value.Revision));
        Assert.Equal(FormFolderErrors.NotFound, (await fixture.Folder.UpdateAsync(hidden.Id, new(1, "Nivel 3", group.Id, null), Ct)).Error);
        Assert.Equal(FormFolderErrors.NotFound, (await fixture.Folder.DeleteAsync(hidden.Id, new(1), Ct)).Error);
        Assert.Equal(FormFolderErrors.NotFound, (await fixture.Folder.CreateAsync(new(hidden.Id, "Nivel 4", null), Ct)).Error);
        Assert.Equal([menu.Id, group.Id, hidden.Id], fixture.Folders.Select(folder => folder.Id));
    }

    [Fact]
    public async Task Deleting_a_folder_takes_its_subfolders_and_leaves_its_items_in_their_other_folders()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var top = fixture.Arrange("Facturación");
        var child = fixture.Arrange("Notas", top);
        var other = fixture.Arrange("Repartos");
        var form = Guid.NewGuid();
        var catalog = Guid.NewGuid();
        fixture.File(top, FormFolderItemKind.Form, form);
        fixture.File(child, FormFolderItemKind.Form, form);
        fixture.File(child, FormFolderItemKind.Catalog, catalog);
        fixture.File(other, FormFolderItemKind.Catalog, catalog);

        var preview = await fixture.Folder.GetDeletionAsync(top.Id, Ct);
        Assert.Equal(new FormFolderDeletionResponse(1, 1, 1, 1), preview.Value);
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
        var empty = fixture.Arrange("Vacía", billing);
        var abc = fixture.Arrange("Abastecimiento");
        var design = Guid.NewGuid();
        var hidden = Guid.NewGuid();
        fixture.File(notes, FormFolderItemKind.Form, design);
        fixture.File(abc, FormFolderItemKind.Form, hidden);
        fixture.File(empty, FormFolderItemKind.Catalog, Guid.NewGuid());
        fixture.Visible[FormFolderListing.Designs].Add(design);

        var reader = (await fixture.Folder.ListAsync(new("designs"), Ct)).Value;
        Assert.False(reader.CanManage);
        Assert.Equal(["facturación", "Notas"], reader.Folders.Select(folder => folder.Name));
        Assert.Equal((billing.Id, (string?)null), (reader.Folders[1].ParentId, reader.Folders[1].Icon));
        Assert.Equal(billing.MenuIcon, reader.Folders[0].Icon);

        fixture.Permissions.Permissions[fixture.Actor] = [P.Read, P.Manage];
        var manager = (await fixture.Folder.ListAsync(new("designs"), Ct)).Value;
        Assert.True(manager.CanManage);
        Assert.Equal(["Abastecimiento", "facturación", "Notas", "Vacía"], manager.Folders.Select(folder => folder.Name));
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task Whoever_only_manages_catalogs_sees_every_folder_to_file_catalogs_but_does_not_administer_them()
    {
        using var fixture = new FormFolderFixture(P.CatalogsRead, P.CatalogsManage);
        var billing = fixture.Arrange("Facturación");
        fixture.Arrange("Notas", billing);

        var tree = (await fixture.Folder.ListAsync(new("catalogs"), Ct)).Value;

        Assert.False(tree.CanManage);
        Assert.Equal(["Facturación", "Notas"], tree.Folders.Select(folder => folder.Name));
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
    public async Task A_missing_revision_is_invalid_and_opens_no_transaction()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var folder = fixture.Arrange("Repartos");

        Assert.True(Assert.IsType<ValidationError>((await fixture.Folder.UpdateAsync(folder.Id, new(null, "Rutas", null, null), Ct)).Error)
            .Errors.ContainsKey("expectedRevision"));
        Assert.True(Assert.IsType<ValidationError>((await fixture.Folder.DeleteAsync(folder.Id, new(0), Ct)).Error)
            .Errors.ContainsKey("expectedRevision"));
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }
}
