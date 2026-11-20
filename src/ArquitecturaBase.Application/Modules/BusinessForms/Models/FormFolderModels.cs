namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

/// <summary>The three listings that show the folder tree (CP1-A): «Formularios», «Diseñar formularios» and «Catálogos».</summary>
public enum FormFolderListing { Forms, Designs, Catalogs }

/// <summary>The tree of a listing: <c>forms</c>, <c>designs</c> or <c>catalogs</c>.</summary>
public sealed record ListFormFoldersRequest(string? Kind);

/// <summary>A new folder: at the first level a menu with the icon asked or the first free one; below, a group without an icon.</summary>
public sealed record CreateFormFolderRequest(Guid? ParentId, string? Name, string? Icon)
{ public override string ToString() => "CreateFormFolderRequest { Content = [REDACTED] }"; }

/// <summary>
/// Renames, moves and changes the icon of a folder. At the first level it takes the icon asked, else the one it had, else (coming from
/// being a subfolder) the first free one; below, the icon has to come empty and the one it had goes away.
/// </summary>
public sealed record UpdateFormFolderRequest(long? ExpectedRevision, string? Name, Guid? ParentId, string? Icon)
{ public override string ToString() => "UpdateFormFolderRequest { Content = [REDACTED] }"; }

public sealed record DeleteFormFolderRequest(long? ExpectedRevision);

/// <summary>The whole set of folders of a form or a catalog, saved against the one that was read (CP2-A).</summary>
public sealed record SetFormItemFoldersRequest(IReadOnlyList<Guid>? ExpectedFolderIds, IReadOnlyList<Guid>? FolderIds);

/// <summary>A folder: <see cref="Icon"/> is not null exactly when it is a menu (no parent).</summary>
public sealed record FormFolderResponse(Guid Id, Guid? ParentId, string Name, long Revision, string? Icon);

/// <summary>The folders a person sees in a listing, in alphabetical order, and whether they administer folders.</summary>
public sealed record FormFolderTreeResponse(IReadOnlyList<FormFolderResponse> Folders, bool CanManage);

/// <summary>What deleting a folder takes along: its subfolders. Its forms and catalogs stay in their other folders or without one.</summary>
public sealed record FormFolderDeletionResponse(long Revision, int SubfolderCount, int FormCount, int CatalogCount);

public sealed record FormItemFoldersResponse(IReadOnlyList<Guid> FolderIds);

/// <summary>
/// The whole set of forms directly in a folder («Agregar formularios», CM2), saved against the one that was read; its catalogs and
/// its subfolders stay as they are.
/// </summary>
public sealed record SetFormFolderFormsRequest(IReadOnlyList<Guid>? ExpectedFormIds, IReadOnlyList<Guid>? FormIds);

/// <summary>The forms (not deleted) directly in a folder, not in its subfolders: drafts and deactivated ones too.</summary>
public sealed record FormFolderFormsResponse(IReadOnlyList<Guid> FormIds);

/// <summary>A form that exists (not deleted) and how many folders it is in.</summary>
public sealed record FormFolderCountRow(Guid FormId, int FolderCount);

/// <summary>
/// The folders of the side menu (CM1 to CM6) for the person reading it, in the order they are shown: a menu per folder of the first
/// level, already pruned for whoever does not administer forms. <see cref="CanManage"/> says whether they do.
/// </summary>
public sealed record FormMenuResponse(bool CanManage, IReadOnlyList<FormMenuSectionResponse> Menus);

/// <summary>
/// A menu: the active forms directly in its folder, then a group per subfolder. <see cref="CatalogCount"/> counts the catalogs of the
/// folder and its subfolders, each once, and is 0 for whoever does not administer forms.
/// </summary>
public sealed record FormMenuSectionResponse(Guid FolderId, string Name, string Icon, long Revision, int CatalogCount,
    IReadOnlyList<FormMenuFormResponse> Forms, IReadOnlyList<FormMenuGroupResponse> Groups);

/// <summary>A group of a menu: a subfolder with the active forms directly inside it.</summary>
public sealed record FormMenuGroupResponse(Guid FolderId, string Name, long Revision, IReadOnlyList<FormMenuFormResponse> Forms);

public sealed record FormMenuFormResponse(Guid Id, string Name);

public sealed record FormFolderRow(Guid Id, Guid? ParentId, string Name, string NormalizedName, string? SystemKey, string? MenuIcon, long Revision);

/// <summary>An active form directly in one of the folders, with its published name in the display culture.</summary>
public sealed record FormFolderMenuFormRow(Guid FolderId, Guid DefinitionId, string Name);

/// <summary>What deleting a folder takes: the folders below it and the different forms (not deleted) and catalogs they hold with it.</summary>
public sealed record FormFolderDeletionCounts(int Subfolders, int Forms, int Catalogs);

/// <summary>How many catalogs (not deleted) a menu holds in its folder and its subfolders, each once.</summary>
public sealed record FormMenuCatalogCountRow(Guid FolderId, int Count);
