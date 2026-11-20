using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using P = ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormsPermissions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// CM2, «Agregar formularios»: the forms directly in a folder are read without the deleted ones and their whole set is saved against
// the one that was read, in one transaction under the folders lock, without touching its catalogs or its subfolders.
public sealed partial class FormFolderServiceTests
{
    [Fact]
    public async Task The_forms_of_a_folder_are_read_without_the_deleted_ones_and_replaced_in_one_transaction()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var folder = fixture.Arrange("Repartos");
        var (kept, removed, added, deleted) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        fixture.File(folder, FormFolderItemKind.Form, kept);
        fixture.File(folder, FormFolderItemKind.Form, removed);
        fixture.File(folder, FormFolderItemKind.Form, deleted);
        fixture.Items.Remove((FormFolderItemKind.Form, deleted));
        fixture.Items.Add((FormFolderItemKind.Form, added));

        var read = await fixture.Folder.GetFormsAsync(folder.Id, Ct);
        var saved = await fixture.Folder.SetFormsAsync(folder.Id, new([removed, kept], [added, kept]), Ct);

        Assert.Equal(new[] { kept, removed }.Order(), read.Value.FormIds.Order());
        Assert.True(saved.IsSuccess);
        Assert.Equal([added, kept], saved.Value.FormIds);
        // A deleted form keeps its membership, hidden, as deleting it left it.
        Assert.Equal(new[] { kept, added, deleted }.Order(), fixture.Memberships.Select(value => value.ItemId).Order());
        Assert.Equal((1, 1, 1), (fixture.UnitOfWork.Transactions, fixture.UnitOfWork.Commits, fixture.Locks));
    }

    [Fact]
    public async Task A_set_of_forms_that_changed_since_it_was_read_is_a_conflict_and_changes_nothing()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var folder = fixture.Arrange("Repartos");
        var (filed, other) = (Guid.NewGuid(), Guid.NewGuid());
        fixture.File(folder, FormFolderItemKind.Form, filed);
        fixture.Items.Add((FormFolderItemKind.Form, other));

        var stale = await fixture.Folder.SetFormsAsync(folder.Id, new([], [other]), Ct);

        Assert.Equal(FormFolderErrors.SetChanged, stale.Error);
        Assert.Equal([filed], fixture.Memberships.Select(value => value.ItemId));
        Assert.Equal(0, fixture.UnitOfWork.Commits);
    }

    // One reads two forms, another deletes one of them and the first saves the set it read plus a third: what changed is the set.
    [Fact]
    public async Task A_set_that_changed_because_a_form_was_deleted_is_a_conflict_before_the_missing_form()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var folder = fixture.Arrange("Repartos");
        var (kept, deleted, added) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        fixture.File(folder, FormFolderItemKind.Form, kept);
        fixture.File(folder, FormFolderItemKind.Form, deleted);
        fixture.Items.Add((FormFolderItemKind.Form, added));
        var read = (await fixture.Folder.GetFormsAsync(folder.Id, Ct)).Value.FormIds;
        fixture.Items.Remove((FormFolderItemKind.Form, deleted));

        var stale = await fixture.Folder.SetFormsAsync(folder.Id, new(read, [kept, deleted, added]), Ct);

        Assert.Equal(FormFolderErrors.SetChanged, stale.Error);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Replacing_the_forms_of_a_folder_leaves_its_catalogs_its_subfolders_and_other_folders_as_they_were()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var folder = fixture.Arrange("Repartos");
        var group = fixture.Arrange("Norte", folder);
        var other = fixture.Arrange("Compras");
        var (form, inGroup, catalog) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        fixture.File(folder, FormFolderItemKind.Form, form);
        fixture.File(other, FormFolderItemKind.Form, form);
        fixture.File(folder, FormFolderItemKind.Catalog, catalog);
        fixture.File(group, FormFolderItemKind.Form, inGroup);

        Assert.Equal([form], (await fixture.Folder.GetFormsAsync(folder.Id, Ct)).Value.FormIds);
        Assert.True((await fixture.Folder.SetFormsAsync(folder.Id, new([form], []), Ct)).IsSuccess);

        Assert.Equal(new[] { (other.Id, form), (folder.Id, catalog), (group.Id, inGroup) }.Order(),
            fixture.Memberships.Select(value => (value.FolderId, value.ItemId)).Order());
    }

    [Fact]
    public async Task More_than_a_thousand_repeated_empty_or_missing_ids_are_invalid_and_open_no_transaction()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var folder = fixture.Arrange("Repartos");
        var form = Guid.NewGuid();
        var thousand = Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in thousand.Append(form)) fixture.Items.Add((FormFolderItemKind.Form, id));

        foreach (var request in new SetFormFolderFormsRequest[]
            { new(null, []), new([], null), new([], [form, form]), new([Guid.Empty], []), new([], thousand.Append(form).ToArray()) })
            Assert.IsType<ValidationError>((await fixture.Folder.SetFormsAsync(folder.Id, request, Ct)).Error);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);

        Assert.True((await fixture.Folder.SetFormsAsync(folder.Id, new([], thousand), Ct)).IsSuccess);
        Assert.Equal(1000, fixture.Memberships.Count);
    }

    [Fact]
    public async Task A_deleted_or_missing_form_and_a_missing_folder_are_not_found_and_save_nothing()
    {
        using var fixture = new FormFolderFixture(P.Manage);
        var folder = fixture.Arrange("Repartos");
        var (form, deleted) = (Guid.NewGuid(), Guid.NewGuid());
        fixture.Items.Add((FormFolderItemKind.Form, form));

        Assert.Equal(FormDefinitionErrors.NotFound, (await fixture.Folder.SetFormsAsync(folder.Id, new([], [form, deleted]), Ct)).Error);
        Assert.Equal(FormFolderErrors.NotFound, (await fixture.Folder.SetFormsAsync(Guid.NewGuid(), new([], [form]), Ct)).Error);
        Assert.Equal(FormFolderErrors.NotFound, (await fixture.Folder.GetFormsAsync(Guid.NewGuid(), Ct)).Error);

        Assert.Empty(fixture.Memberships);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_form_already_in_the_most_folders_is_refused_as_saving_its_own_folders_would_be()
    {
        using var fixture = new FormFolderFixture(P.Read, P.Manage);
        var form = Guid.NewGuid();
        for (var index = 0; index < 100; index++) fixture.File(fixture.Arrange("Carpeta " + index), FormFolderItemKind.Form, form);
        var full = fixture.Folders.Select(folder => folder.Id).ToArray();
        var another = fixture.Arrange("Una más");

        var refused = Assert.IsType<ValidationError>((await fixture.Folder.SetFormsAsync(another.Id, new([], [form]), Ct)).Error);
        var asItem = Assert.IsType<ValidationError>((await fixture.ItemFolders.SetAsync(FormFolderItemKind.Form, form,
            new(full, [.. full, another.Id]), Ct)).Error);

        Assert.Equal(asItem.Code, refused.Code);
        Assert.Equal(asItem.Errors["folderIds"], refused.Errors["formIds"]);
        // Kept where it already is, it adds no folder.
        Assert.True((await fixture.Folder.SetFormsAsync(full[0], new([form], [form]), Ct)).IsSuccess);
        Assert.Equal(100, fixture.Memberships.Count);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
    }
}
