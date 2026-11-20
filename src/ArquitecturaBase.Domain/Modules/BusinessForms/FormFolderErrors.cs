using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public static class FormFolderErrors
{
    public static readonly Error NotFound = Error.NotFound("Forms.Folder.NotFound", "The folder was not found.");
    public static readonly Error NameInvalid = Error.Validation("Forms.Folder.NameInvalid", "Enter a folder name of up to 80 characters.");
    public static readonly Error NameTaken = Error.Validation("Forms.Folder.NameTaken", "A folder with this name already exists here.");
    public static readonly Error TooDeep = Error.Validation("Forms.Folder.TooDeep", "A folder goes in the menu or inside a folder of the menu: there is no third level, and a folder with subfolders cannot go inside another.");
    public static readonly Error TooMany = Error.Validation("Forms.Folder.TooMany", "There are already 500 folders. Remove one before creating another.");
    public static readonly Error Cycle = Error.Validation("Forms.Folder.Cycle", "A folder cannot go inside itself or one of its subfolders.");
    public static readonly Error IconInvalid = Error.Validation("Forms.Folder.IconInvalid", "A folder of the menu takes one of the twelve icons and a subfolder takes none.");
    public static readonly Error RevisionConflict = Error.Conflict("Forms.Folder.RevisionConflict", "The folder changed. Reload it and try again.");
    public static readonly Error SetChanged = Error.Conflict("Forms.Folder.SetChanged", "The folders changed while they were being edited. Reload them and try again.");
    public static readonly Error RequestInvalid = Error.Validation("Forms.Folder.RequestInvalid", "The folder request is invalid.");
}
