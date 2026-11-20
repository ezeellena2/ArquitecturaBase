using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

// CP1-A, CP2-A, CP3-A and CM1 to CM6: the folders of forms and catalogs; a folder of the first level is a menu with an icon.

/// <summary>A new folder: at the first level, a menu with one of the twelve icons (the first free one if none comes); below, without one.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateFormFolderHttpRequest(Guid? ParentId, string? Name, string? Icon)
{
    public override string ToString() => "CreateFormFolderHttpRequest { Content = [REDACTED] }";
}

/// <summary>
/// Renames, moves and changes the icon of a folder. At the first level it keeps its icon if none comes (or takes the first free one if
/// it was a subfolder); below, the icon comes empty.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateFormFolderHttpRequest(long? ExpectedRevision, string? Name, Guid? ParentId, string? Icon)
{
    public override string ToString() => "UpdateFormFolderHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteFormFolderHttpRequest(long? ExpectedRevision);

/// <summary>The whole set of folders of a form or a catalog, with the one that was read.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetFormItemFoldersHttpRequest(IReadOnlyList<Guid>? ExpectedFolderIds, IReadOnlyList<Guid>? FolderIds);

/// <summary>The whole set of forms directly in a folder, with the one that was read (CM2).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetFormFolderFormsHttpRequest(IReadOnlyList<Guid>? ExpectedFormIds, IReadOnlyList<Guid>? FormIds);
