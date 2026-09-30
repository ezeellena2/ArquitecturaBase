using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Settings;

public static class SettingsErrors
{
    public const string NotFoundCode = "Settings.System.NotFound";
    public const string RevisionConflictCode = "Settings.System.RevisionConflict";
    public const string CultureInvalidCode = "Settings.System.CultureInvalid";
    public const string PageSizeInvalidCode = "Settings.System.PageSizeInvalid";

    public static readonly Error RevisionConflict = Error.Conflict(RevisionConflictCode, "The system settings have changed. Review the current values.");
    public static readonly Error CultureInvalid = Error.Validation(CultureInvalidCode, "The default culture is not supported.");
    public static readonly Error PageSizeInvalid = Error.Validation(PageSizeInvalidCode, "The default page size is not supported.");

    /// <summary>No existe la fila de ajustes. La crea el seed: si falta, la base quedó a medio preparar.</summary>
    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The system settings were not found.");
}
