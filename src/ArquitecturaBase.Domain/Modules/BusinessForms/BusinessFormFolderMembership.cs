using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

/// <summary>What a folder holds: a form or a catalog.</summary>
public enum FormFolderItemKind { Form, Catalog }

/// <summary>
/// A form or a catalog inside a folder (CP2-A). The same item can be in several folders; the pair is unique and goes away with its
/// folder. Deleting the item logically only hides it: its memberships stay.
/// </summary>
public sealed class BusinessFormFolderMembership : Entity
{
    private BusinessFormFolderMembership() { }

    public Guid FolderId { get; private set; }
    public FormFolderItemKind ItemKind { get; private set; }
    public Guid ItemId { get; private set; }

    public static BusinessFormFolderMembership Create(Guid folderId, FormFolderItemKind kind, Guid itemId)
    {
        if (folderId == Guid.Empty || itemId == Guid.Empty || !Enum.IsDefined(kind))
            throw new ArgumentException("The folder membership is invalid.");
        return new BusinessFormFolderMembership { FolderId = folderId, ItemKind = kind, ItemId = itemId };
    }
}
