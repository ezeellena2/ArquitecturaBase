using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

/// <summary>A folder of the tree as its rules need it.</summary>
public sealed record FormFolderNode(Guid Id, Guid? ParentId, string NormalizedName);

/// <summary>What changes in the folders of an item.</summary>
public sealed record FormFolderMembershipPlan(IReadOnlyList<Guid> Added, IReadOnlyList<Guid> Removed);

/// <summary>
/// The rules of the whole tree, over every folder read under the folders lock: an existing parent, up to
/// <see cref="BusinessFormFolder.MaxDepth"/> levels counting the subfolders that move along, no cycles and a unique name per parent.
/// </summary>
public static class FormFolderTree
{
    /// <summary>Whether <paramref name="folderId"/> (null for a new folder) may live under <paramref name="parentId"/> with that name.</summary>
    public static Result CheckPlacement(Guid? folderId, Guid? parentId, string normalizedName, IReadOnlyCollection<FormFolderNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var byId = nodes.ToDictionary(node => node.Id);
        if (parentId is { } parent && !byId.ContainsKey(parent)) return FormFolderErrors.NotFound;
        var height = 1;
        if (folderId is { } id)
        {
            var subtree = Subtree(id, nodes);
            if (parentId is { } target && subtree.Contains(target)) return FormFolderErrors.Cycle;
            height = Height(id, nodes);
        }
        if (Level(parentId, byId) + height > BusinessFormFolder.MaxDepth) return FormFolderErrors.TooDeep;
        return nodes.Any(node => node.ParentId == parentId && node.Id != folderId && node.NormalizedName == normalizedName)
            ? FormFolderErrors.NameTaken : Result.Success();
    }

    /// <summary>The folder and every folder below it.</summary>
    public static IReadOnlySet<Guid> Subtree(Guid id, IReadOnlyCollection<FormFolderNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var children = nodes.Where(node => node.ParentId is not null).ToLookup(node => node.ParentId!.Value, node => node.Id);
        var found = new HashSet<Guid> { id };
        var pending = new Queue<Guid>([id]);
        while (pending.TryDequeue(out var current))
            foreach (var child in children[current])
                if (found.Add(child)) pending.Enqueue(child);
        return found;
    }

    /// <summary>
    /// The items' folders change only if they are still the ones that were read (<paramref name="expected"/>, in any order); otherwise
    /// <see cref="FormFolderErrors.SetChanged"/>.
    /// </summary>
    public static Result<FormFolderMembershipPlan> PlanMemberships(IReadOnlyCollection<Guid> current, IReadOnlyCollection<Guid> expected,
        IReadOnlyCollection<Guid> target)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(target);
        if (!current.ToHashSet().SetEquals(expected)) return FormFolderErrors.SetChanged;
        return new FormFolderMembershipPlan(target.Except(current).ToArray(), current.Except(target).ToArray());
    }

    // The levels a folder carries: 1 for a leaf.
    private static int Height(Guid id, IReadOnlyCollection<FormFolderNode> nodes)
    {
        var children = nodes.Where(node => node.ParentId is not null).ToLookup(node => node.ParentId!.Value, node => node.Id);
        var height = 0;
        var level = new List<Guid> { id };
        while (level.Count > 0 && height <= BusinessFormFolder.MaxDepth + 1)
        {
            height++;
            level = level.SelectMany(current => children[current]).ToList();
        }
        return height;
    }

    // The level of the parent: 0 for the top of the tree.
    private static int Level(Guid? parentId, Dictionary<Guid, FormFolderNode> byId)
    {
        var level = 0;
        for (var current = parentId; current is { } id && byId.TryGetValue(id, out var node) && level <= BusinessFormFolder.MaxDepth + 1; current = node.ParentId)
            level++;
        return level;
    }
}
