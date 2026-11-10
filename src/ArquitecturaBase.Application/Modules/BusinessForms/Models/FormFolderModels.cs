namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

/// <summary>The three listings that show the folder tree (CP1-A): «Formularios», «Diseñar formularios» and «Catálogos».</summary>
public enum FormFolderListing { Forms, Designs, Catalogs }

/// <summary>The tree of a listing: <c>forms</c>, <c>designs</c> or <c>catalogs</c>.</summary>
public sealed record ListFormFoldersRequest(string? Kind);

public sealed record CreateFormFolderRequest(Guid? ParentId, string? Name)
{ public override string ToString() => "CreateFormFolderRequest { Content = [REDACTED] }"; }

/// <summary>Renames and moves a folder: the same parent renames it and the same name moves it.</summary>
public sealed record UpdateFormFolderRequest(long? ExpectedRevision, string? Name, Guid? ParentId)
{ public override string ToString() => "UpdateFormFolderRequest { Content = [REDACTED] }"; }

public sealed record DeleteFormFolderRequest(long? ExpectedRevision);

/// <summary>Its place in the menu (CP5-A): <c>None</c>, <c>Own</c> (with an icon), <c>Forms</c> or <c>Administration</c>.</summary>
public sealed record SetFormFolderMenuRequest(long? ExpectedRevision, string? Placement, string? Icon);

/// <summary>The whole set of folders of a form or a catalog, saved against the one that was read (CP2-A).</summary>
public sealed record SetFormItemFoldersRequest(IReadOnlyList<Guid>? ExpectedFolderIds, IReadOnlyList<Guid>? FolderIds);

public sealed record FormFolderResponse(Guid Id, Guid? ParentId, string Name, long Revision, string MenuPlacement, string? MenuIcon);

/// <summary>The folders a person sees in a listing, in alphabetical order, and whether they administer folders.</summary>
public sealed record FormFolderTreeResponse(IReadOnlyList<FormFolderResponse> Folders, bool CanManage);

/// <summary>What deleting a folder takes along: its subfolders. Its forms and catalogs stay in their other folders or without one.</summary>
public sealed record FormFolderDeletionResponse(long Revision, int SubfolderCount, int FormCount, int CatalogCount);

public sealed record FormItemFoldersResponse(IReadOnlyList<Guid> FolderIds);

/// <summary>
/// The side menu of the folders (CP5-A) for the person reading it: the own menus, and the groups of «Gestión de formularios» and
/// «Administración», with their active forms already pruned and in the order they are shown.
/// </summary>
public sealed record FormMenuResponse(IReadOnlyList<FormMenuSectionResponse> Menus, IReadOnlyList<FormMenuGroupResponse> Forms,
    IReadOnlyList<FormMenuGroupResponse> Administration);

/// <summary>An own menu: its forms, then a group per subfolder with what is below it.</summary>
public sealed record FormMenuSectionResponse(Guid FolderId, string Name, string Icon, IReadOnlyList<FormMenuFormResponse> Forms,
    IReadOnlyList<FormMenuGroupResponse> Groups);

public sealed record FormMenuGroupResponse(Guid FolderId, string Name, IReadOnlyList<FormMenuFormResponse> Forms);

public sealed record FormMenuFormResponse(Guid Id, string Name);

public sealed record FormFolderRow(Guid Id, Guid? ParentId, string Name, string NormalizedName, string? SystemKey, string MenuPlacement,
    string? MenuIcon, long Revision);

/// <summary>An active form directly in one of the folders, with its published name in the display culture.</summary>
public sealed record FormFolderMenuFormRow(Guid FolderId, Guid DefinitionId, string Name);

public sealed record FormFolderItemCounts(int Forms, int Catalogs);
