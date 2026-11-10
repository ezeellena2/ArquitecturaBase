using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

/// <summary>Where a folder goes in the side menu (CP5-A): nowhere, its own menu, or a group inside «Gestión de formularios» or «Administración».</summary>
public enum FormFolderMenuPlacement { None, Own, Forms, Administration }

/// <summary>
/// A folder of forms and catalogs (CP1-A). It only orders: it never grants or takes away access. Its name is unique per parent without
/// telling case apart; the levels and cycles are checked against the whole tree by <see cref="FormFolderTree"/>. Rename, move, delete
/// and its place in the menu carry its revision. <see cref="SystemKey"/> remembers the folder the system created (ARCA, the library), so
/// what the system files again goes back to it even after a rename.
/// </summary>
public sealed class BusinessFormFolder : Entity, IAuditable
{
    public const int NameMaxLength = 80;
    public const int SystemKeyMaxLength = 64;
    public const int MaxDepth = 5;
    public const int MenuIconMaxLength = 16;
    public const string DefaultMenuIcon = "folder";

    /// <summary>The twelve generic icons of an own menu, the folder first (what the menu shows when it is collapsed).</summary>
    public static IReadOnlyList<string> MenuIcons { get; } =
        ["folder", "truck", "package", "receipt", "users", "calendar", "clipboard", "chart", "wrench", "store", "leaf", "file"];

    private BusinessFormFolder() { }

    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = "";
    public string NormalizedName { get; private set; } = "";
    public string? SystemKey { get; private set; }
    public FormFolderMenuPlacement MenuPlacement { get; private set; }
    public string? MenuIcon { get; private set; }
    public long Revision { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static Result<BusinessFormFolder> Create(Guid? parentId, string? name, string? systemKey = null)
    {
        if (Clean(name) is not { } clean) return FormFolderErrors.NameInvalid;
        if (systemKey is not null && (string.IsNullOrWhiteSpace(systemKey) || systemKey.Length > SystemKeyMaxLength))
            throw new ArgumentException("The system key is invalid.", nameof(systemKey));
        return new BusinessFormFolder { ParentId = parentId, Name = clean, NormalizedName = NormalizeName(clean), SystemKey = systemKey };
    }

    /// <summary>Renames and moves it. The same name and parent change nothing, not even the revision.</summary>
    public Result Update(long expectedRevision, string? name, Guid? parentId)
    {
        if (CheckRevision(expectedRevision) is { IsFailure: true } stale) return stale;
        if (Clean(name) is not { } clean) return FormFolderErrors.NameInvalid;
        if (clean == Name && parentId == ParentId) return Result.Success();
        Name = clean; NormalizedName = NormalizeName(clean); ParentId = parentId; Revision++;
        return Result.Success();
    }

    /// <summary>Its place in the menu. An own menu takes one of <see cref="MenuIcons"/>, the folder by default; the others take none.</summary>
    public Result SetMenu(long expectedRevision, FormFolderMenuPlacement placement, string? icon)
    {
        if (CheckRevision(expectedRevision) is { IsFailure: true } stale) return stale;
        if (!Enum.IsDefined(placement)) return FormFolderErrors.RequestInvalid;
        if (placement == FormFolderMenuPlacement.Own ? icon is not null && !MenuIcons.Contains(icon) : icon is not null)
            return FormFolderErrors.IconInvalid;
        var chosen = placement == FormFolderMenuPlacement.Own ? icon ?? DefaultMenuIcon : null;
        if (placement == MenuPlacement && chosen == MenuIcon) return Result.Success();
        MenuPlacement = placement; MenuIcon = chosen; Revision++;
        return Result.Success();
    }

    public Result CheckRevision(long expectedRevision) =>
        expectedRevision == Revision && Revision < 9007199254740991 ? Result.Success() : FormFolderErrors.RevisionConflict;

    /// <summary>A folder the system adopts as its own: a top folder with the name it would have created.</summary>
    public void AssignSystemKey(string systemKey)
    {
        if (SystemKey is not null || string.IsNullOrWhiteSpace(systemKey) || systemKey.Length > SystemKeyMaxLength)
            throw new InvalidOperationException("The folder cannot take this system key.");
        SystemKey = systemKey;
    }

    public static string NormalizeName(string name) =>
        string.Join(" ", name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    /// <summary>A name that is not blank once trimmed, fits <see cref="NameMaxLength"/> and can be stored.</summary>
    public static bool IsValidName(string? name) => Clean(name) is not null;

    private static string? Clean(string? name)
    {
        var clean = name?.Trim();
        return string.IsNullOrEmpty(clean) || clean.Length > NameMaxLength || !StorableText.IsStorable(clean) ? null : clean;
    }

    public override string ToString() => $"BusinessFormFolder {{ Id = {Id}, Revision = {Revision} }}";
}
