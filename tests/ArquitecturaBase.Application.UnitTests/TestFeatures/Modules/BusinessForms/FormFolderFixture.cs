using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

/// <summary>
/// The folders in memory, as repository and reader at once. Writes and the lock fail outside the transaction; deleting a folder takes
/// its subfolders and memberships along, as the database cascade does. The reader reads the two levels the real one reads. What each
/// listing shows, the active forms and the ones the person can open from the menu are set by hand.
/// </summary>
internal sealed class FormFolderFixture : IFormFolderRepository, IFormFolderReader, IDisposable
{
    private readonly ServiceProvider _provider;
    public InMemoryUserAccounts Users { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public FakePermissionService Permissions { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public List<BusinessFormFolder> Folders { get; } = [];
    public List<BusinessFormFolderMembership> Memberships { get; } = [];
    public HashSet<(FormFolderItemKind Kind, Guid Id)> Items { get; } = [];
    public Dictionary<FormFolderListing, HashSet<Guid>> Visible { get; } = new()
    {
        [FormFolderListing.Forms] = [], [FormFolderListing.Designs] = [], [FormFolderListing.Catalogs] = [],
    };
    public Dictionary<Guid, string> ActiveForms { get; } = [];
    public HashSet<Guid> Openable { get; } = [];
    public HashSet<Guid> Catalogs { get; } = [];
    public int Locks { get; private set; }
    public Guid Actor { get; }

    public FormFolderFixture(params string[] permissions)
    {
        Actor = Users.AddUser("folders@example.invalid").Id;
        Current.UserId = Actor;
        Permissions.Permissions[Actor] = permissions;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBusinessFormsApplication();
        services.AddSingleton<ICurrentUser>(Current);
        services.AddSingleton<IPermissionService>(Permissions);
        services.AddSingleton<IUserReader>(Users);
        services.AddSingleton<IUnitOfWork>(UnitOfWork);
        services.AddSingleton<IFormFolderRepository>(this);
        services.AddSingleton<IFormFolderReader>(this);
        services.AddScoped<IRequestValidator, RequestValidator>();
        _provider = services.BuildServiceProvider();
    }

    public IFormFolderService Folder => _provider.GetRequiredService<IFormFolderService>();
    public IFormFolderItemService ItemFolders => _provider.GetRequiredService<IFormFolderItemService>();
    public IFormMenuService Menu => _provider.GetRequiredService<IFormMenuService>();
    public FormSystemFolderLinker Linker => _provider.GetRequiredService<FormSystemFolderLinker>();

    /// <summary>A folder of the menu with the icon asked or the first free one, or a subfolder of <paramref name="parent"/>.</summary>
    public BusinessFormFolder Arrange(string name, BusinessFormFolder? parent = null, string? icon = null)
    {
        var folder = BusinessFormFolder.Create(parent?.Id, name, parent is null ? icon ?? BusinessFormFolder.FirstFreeIcon(Folders) : null).Value;
        Folders.Add(folder);
        return folder;
    }

    public void File(BusinessFormFolder folder, FormFolderItemKind kind, Guid itemId)
    {
        Items.Add((kind, itemId));
        if (kind == FormFolderItemKind.Catalog) Catalogs.Add(itemId);
        Memberships.Add(BusinessFormFolderMembership.Create(folder.Id, kind, itemId));
    }

    public Task LockAsync(CancellationToken cancellationToken)
    {
        RequireTransaction();
        Locks++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BusinessFormFolder>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BusinessFormFolder>>(Folders.ToArray());

    public Task<IReadOnlyList<BusinessFormFolderMembership>> ListMembershipsAsync(FormFolderItemKind kind, Guid itemId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BusinessFormFolderMembership>>(Memberships.Where(value => value.ItemKind == kind && value.ItemId == itemId).ToArray());

    public Task<IReadOnlyList<BusinessFormFolderMembership>> ListFolderMembershipsAsync(Guid folderId, FormFolderItemKind kind, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BusinessFormFolderMembership>>(Memberships.Where(value => value.FolderId == folderId && value.ItemKind == kind).ToArray());

    public void Add(BusinessFormFolder folder) { RequireTransaction(); Folders.Add(folder); }

    public void Add(BusinessFormFolderMembership membership) { RequireTransaction(); Memberships.Add(membership); }

    public void Remove(BusinessFormFolder folder)
    {
        RequireTransaction();
        var subtree = FormFolderTree.Subtree(folder.Id, Nodes());
        Folders.RemoveAll(value => subtree.Contains(value.Id));
        Memberships.RemoveAll(value => subtree.Contains(value.FolderId));
    }

    public void Remove(BusinessFormFolderMembership membership) { RequireTransaction(); Memberships.Remove(membership); }

    Task<IReadOnlyList<FormFolderRow>> IFormFolderReader.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormFolderRow>>(BusinessFormFolder.TwoLevels(Folders).Select(folder => new FormFolderRow(folder.Id,
            folder.ParentId, folder.Name, folder.NormalizedName, folder.SystemKey, folder.ShownIcon, folder.Revision)).ToArray());

    public Task<IReadOnlyList<Guid>> ListFolderIdsWithItemsAsync(FormFolderListing listing, Guid actorUserId, bool canReadRecords, bool canDecide,
        bool canCreate, CancellationToken cancellationToken)
    {
        var kind = listing == FormFolderListing.Catalogs ? FormFolderItemKind.Catalog : FormFolderItemKind.Form;
        return Task.FromResult<IReadOnlyList<Guid>>(Memberships.Where(value => value.ItemKind == kind && Visible[listing].Contains(value.ItemId))
            .Select(value => value.FolderId).Distinct().ToArray());
    }

    public Task<IReadOnlyList<Guid>> ListItemFolderIdsAsync(FormFolderItemKind kind, Guid itemId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Memberships.Where(value => value.ItemKind == kind && value.ItemId == itemId).Select(value => value.FolderId).ToArray());

    // A form not in Items is deleted: its memberships stay, hidden.
    public Task<IReadOnlyList<Guid>> ListFolderFormIdsAsync(Guid folderId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Memberships.Where(value => value.FolderId == folderId && value.ItemKind == FormFolderItemKind.Form
            && Items.Contains((FormFolderItemKind.Form, value.ItemId))).Select(value => value.ItemId).ToArray());

    public Task<IReadOnlyList<FormFolderCountRow>> ListFormFolderCountsAsync(IReadOnlyCollection<Guid> formIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormFolderCountRow>>(formIds.Where(id => Items.Contains((FormFolderItemKind.Form, id)))
            .Select(id => new FormFolderCountRow(id, Memberships.Count(value => value.ItemKind == FormFolderItemKind.Form && value.ItemId == id))).ToArray());

    public Task<bool> ExistsItemAsync(FormFolderItemKind kind, Guid itemId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Contains((kind, itemId)));

    public Task<FormFolderDeletionCounts> CountDeletionAsync(Guid folderId, CancellationToken cancellationToken)
    {
        var subtree = FormFolderTree.Subtree(folderId, Nodes());
        var inside = Memberships.Where(value => subtree.Contains(value.FolderId)).ToArray();
        return Task.FromResult(new FormFolderDeletionCounts(subtree.Count - 1,
            inside.Where(value => value.ItemKind == FormFolderItemKind.Form).Select(value => value.ItemId).Distinct().Count(),
            inside.Where(value => value.ItemKind == FormFolderItemKind.Catalog).Select(value => value.ItemId).Distinct().Count()));
    }

    public Task<IReadOnlyList<FormFolderMenuFormRow>> ListMenuFormsAsync(IReadOnlyCollection<Guid> folderIds, string culture, Guid actorUserId, bool canReadRecords, bool canCreate, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormFolderMenuFormRow>>(MenuForms(folderIds, Openable.Contains));

    public Task<IReadOnlyList<FormFolderMenuFormRow>> ListActiveMenuFormsAsync(IReadOnlyCollection<Guid> folderIds, string culture, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormFolderMenuFormRow>>(MenuForms(folderIds, _ => true));

    // A menu is a folder of the first level: its catalogs and those of its subfolders, each once.
    public Task<IReadOnlyList<FormMenuCatalogCountRow>> ListMenuCatalogCountsAsync(CancellationToken cancellationToken)
    {
        var parents = Folders.ToDictionary(folder => folder.Id, folder => folder.ParentId);
        return Task.FromResult<IReadOnlyList<FormMenuCatalogCountRow>>(Memberships
            .Where(value => value.ItemKind == FormFolderItemKind.Catalog && Catalogs.Contains(value.ItemId) && parents.ContainsKey(value.FolderId))
            .GroupBy(value => parents[value.FolderId] ?? value.FolderId)
            .Select(menu => new FormMenuCatalogCountRow(menu.Key, menu.Select(value => value.ItemId).Distinct().Count())).ToArray());
    }

    private FormFolderMenuFormRow[] MenuForms(IReadOnlyCollection<Guid> folderIds, Func<Guid, bool> canOpen) => Memberships
        .Where(value => value.ItemKind == FormFolderItemKind.Form && folderIds.Contains(value.FolderId) && ActiveForms.ContainsKey(value.ItemId) && canOpen(value.ItemId))
        .Select(value => new FormFolderMenuFormRow(value.FolderId, value.ItemId, ActiveForms[value.ItemId])).ToArray();

    public FormFolderNode[] Nodes() => Folders.Select(value => new FormFolderNode(value.Id, value.ParentId, value.NormalizedName)).ToArray();

    private void RequireTransaction()
    {
        if (!UnitOfWork.InTransaction) throw new InvalidOperationException("Folder writes and locks need the transaction.");
    }

    public void Dispose() => _provider.Dispose();
}
