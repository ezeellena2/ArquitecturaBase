using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

/// <summary>
/// A folder of forms and catalogs (CP1-A). It only orders: it never grants or takes away access. A folder of the first level is a
/// menu of the side menu (CM1 to CM6) and always carries one of the <see cref="MenuIcons"/>; a subfolder is a group of its panel and
/// carries none. Its name is unique per parent without telling case apart; the two levels and the cycles are checked against the whole
/// tree by <see cref="FormFolderTree"/>. Rename, move, the icon and delete carry its revision. <see cref="SystemKey"/> remembers the
/// folder the system created (ARCA, the library), so what the system files again goes back to it even after a rename.
/// </summary>
public sealed class BusinessFormFolder : Entity, IAuditable
{
    public const int NameMaxLength = 80;
    public const int SystemKeyMaxLength = 64;
    public const int MaxDepth = 2;
    // A limit on the configuration the tree holds (ADR 0018): the menu and the placement checks walk it in every request (F47).
    public const int MaxFolders = 500;
    public const int MenuIconMaxLength = 16;
    public const string DefaultMenuIcon = "folder";

    /// <summary>The twelve generic icons of a menu, in the order they are offered: the first one no menu uses goes to a new one.</summary>
    public static IReadOnlyList<string> MenuIcons { get; } =
        ["folder", "truck", "package", "receipt", "users", "calendar", "clipboard", "chart", "wrench", "store", "leaf", "file"];

    private BusinessFormFolder() { }

    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = "";
    public string NormalizedName { get; private set; } = "";
    public string? SystemKey { get; private set; }
    public string? MenuIcon { get; private set; }
    public long Revision { get; private set; } = 1;

    /// <summary>
    /// The icon it shows, one of the <see cref="MenuIcons"/> at the first level and none below. Until the second deployment of
    /// FoldersAsMenus the previous version may still leave a menu without an icon, which shows the folder, or a subfolder with one, which
    /// shows none.
    /// </summary>
    public string? ShownIcon => ParentId is null ? MenuIcon ?? DefaultMenuIcon : null;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    /// <summary>A new folder: at the first level with one of the <see cref="MenuIcons"/>, below without an icon.</summary>
    public static Result<BusinessFormFolder> Create(Guid? parentId, string? name, string? icon, string? systemKey = null)
    {
        if (Clean(name) is not { } clean) return FormFolderErrors.NameInvalid;
        if (!FitsLevel(parentId, icon)) return FormFolderErrors.IconInvalid;
        if (systemKey is not null && (string.IsNullOrWhiteSpace(systemKey) || systemKey.Length > SystemKeyMaxLength))
            throw new ArgumentException("The system key is invalid.", nameof(systemKey));
        return new BusinessFormFolder
        {
            ParentId = parentId, Name = clean, NormalizedName = NormalizeName(clean), SystemKey = systemKey, MenuIcon = icon,
        };
    }

    /// <summary>
    /// Renames it, moves it and changes its icon, which has to fit the level it ends in: one of the <see cref="MenuIcons"/> at the
    /// first level, none below. The same name, parent and the icon it shows change nothing, not even the revision.
    /// </summary>
    public Result Update(long expectedRevision, string? name, Guid? parentId, string? icon)
    {
        if (CheckRevision(expectedRevision) is { IsFailure: true } stale) return stale;
        if (Clean(name) is not { } clean) return FormFolderErrors.NameInvalid;
        if (!FitsLevel(parentId, icon)) return FormFolderErrors.IconInvalid;
        if (clean == Name && parentId == ParentId && icon == ShownIcon) return Result.Success();
        Name = clean; NormalizedName = NormalizeName(clean); ParentId = parentId; MenuIcon = icon; Revision++;
        return Result.Success();
    }

    /// <summary>The first of the <see cref="MenuIcons"/> that no folder of the first level shows; once all are taken, the folder.</summary>
    public static string FirstFreeIcon(IEnumerable<BusinessFormFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var used = folders.Where(folder => folder.ParentId is null).Select(folder => folder.ShownIcon).ToHashSet();
        return MenuIcons.FirstOrDefault(icon => !used.Contains(icon)) ?? DefaultMenuIcon;
    }

    /// <summary>
    /// The tree that is read and written (CM1 to CM6): the folders of the first two levels, each with its <see cref="ShownIcon"/>. Until
    /// the second deployment of FoldersAsMenus the previous version may still write below the second level; that is neither shown nor
    /// counted by the rules, and only deleting a folder above it takes it along.
    /// </summary>
    public static IReadOnlyList<BusinessFormFolder> TwoLevels(IReadOnlyCollection<BusinessFormFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var menus = folders.Where(folder => folder.ParentId is null).Select(folder => folder.Id).ToHashSet();
        return folders.Where(folder => folder.ParentId is not { } parent || menus.Contains(parent)).ToArray();
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

    // A menu (no parent) carries one of the twelve icons; a group of its panel carries none.
    private static bool FitsLevel(Guid? parentId, string? icon) => parentId is null ? icon is not null && MenuIcons.Contains(icon) : icon is null;

    private static string? Clean(string? name)
    {
        var clean = name?.Trim();
        return string.IsNullOrEmpty(clean) || clean.Length > NameMaxLength || !StorableText.IsStorable(clean) ? null : clean;
    }

    public override string ToString() => $"BusinessFormFolder {{ Id = {Id}, Revision = {Revision} }}";
}
