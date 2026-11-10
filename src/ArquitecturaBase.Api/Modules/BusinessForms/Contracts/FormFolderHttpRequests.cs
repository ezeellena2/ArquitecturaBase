using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

// CP1-A, CP2-A, CP3-A and CP5-A: the folders of forms and catalogs.

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateFormFolderHttpRequest(Guid? ParentId, string? Name)
{
    public override string ToString() => "CreateFormFolderHttpRequest { Content = [REDACTED] }";
}

/// <summary>Renames and moves a folder: the same parent renames it and the same name moves it.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateFormFolderHttpRequest(long? ExpectedRevision, string? Name, Guid? ParentId)
{
    public override string ToString() => "UpdateFormFolderHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteFormFolderHttpRequest(long? ExpectedRevision);

/// <summary>Its place in the menu: <c>None</c>, <c>Own</c> (with one of the twelve icons), <c>Forms</c> or <c>Administration</c>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetFormFolderMenuHttpRequest(long? ExpectedRevision, string? Placement, string? Icon);

/// <summary>The whole set of folders of a form or a catalog, with the one that was read.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetFormItemFoldersHttpRequest(IReadOnlyList<Guid>? ExpectedFolderIds, IReadOnlyList<Guid>? FolderIds);
