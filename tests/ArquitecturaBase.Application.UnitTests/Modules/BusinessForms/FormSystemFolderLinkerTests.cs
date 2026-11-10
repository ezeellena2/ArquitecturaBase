using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// What the system files (ARCA, the library) goes to the folder it created, found by its key even after a rename, and created again
// at the top if someone deleted it. It runs inside its caller's transaction and never saves.
public sealed class FormSystemFolderLinkerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_system_folder_is_created_once_and_found_again_by_its_key_after_a_rename()
    {
        using var fixture = new FormFolderFixture();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await fixture.UnitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            Assert.True((await fixture.Linker.FileAsync(FormFolderItemKind.Catalog, first, "arca", "ARCA", ct)).IsSuccess);
            return await fixture.Linker.FileAsync(FormFolderItemKind.Catalog, first, "arca", "ARCA", ct);
        }, CommitPolicy.OnSuccess, Ct);
        var folder = Assert.Single(fixture.Folders);
        Assert.Equal(("ARCA", "arca", (Guid?)null), (folder.Name, folder.SystemKey, folder.ParentId));
        Assert.Equal(FormFolderMenuPlacement.None, folder.MenuPlacement);
        Assert.Single(fixture.Memberships);
        Assert.True(folder.Update(1, "Impuestos", null).IsSuccess);

        await fixture.UnitOfWork.ExecuteInTransactionAsync(ct => fixture.Linker.FileAsync(FormFolderItemKind.Catalog, second, "arca", "ARCA", ct),
            CommitPolicy.OnSuccess, Ct);

        Assert.Single(fixture.Folders);
        Assert.Equal(new[] { first, second }.Order(), fixture.Memberships.Where(value => value.FolderId == folder.Id).Select(value => value.ItemId).Order());
        Assert.Equal((3, 2), (fixture.Locks, fixture.UnitOfWork.Commits));
    }

    [Fact]
    public async Task A_deleted_system_folder_is_created_again_and_a_same_named_top_folder_is_adopted()
    {
        using var fixture = new FormFolderFixture();
        var own = fixture.Arrange("arca");
        var catalog = Guid.NewGuid();

        await fixture.UnitOfWork.ExecuteInTransactionAsync(ct => fixture.Linker.FileAsync(FormFolderItemKind.Catalog, catalog, "arca", "ARCA", ct),
            CommitPolicy.OnSuccess, Ct);

        Assert.Equal("arca", Assert.Single(fixture.Folders).SystemKey);
        Assert.Equal(own.Id, Assert.Single(fixture.Memberships).FolderId);

        fixture.Folders.Clear();
        fixture.Memberships.Clear();
        await fixture.UnitOfWork.ExecuteInTransactionAsync(ct => fixture.Linker.FileAsync(FormFolderItemKind.Catalog, catalog, "arca", "ARCA", ct),
            CommitPolicy.OnSuccess, Ct);

        var again = Assert.Single(fixture.Folders);
        Assert.NotEqual(own.Id, again.Id);
        Assert.Equal(("ARCA", "arca"), (again.Name, again.SystemKey));
        Assert.Equal(again.Id, Assert.Single(fixture.Memberships).FolderId);
    }

    [Fact]
    public async Task Filing_outside_a_transaction_fails()
    {
        using var fixture = new FormFolderFixture();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Linker.FileAsync(FormFolderItemKind.Form, Guid.NewGuid(), "arca", "ARCA", Ct));
    }
}
