using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Settings;

public static class SettingsErrors
{
    public const string NotFoundCode = "Settings.System.NotFound";

    /// <summary>No existe la fila de ajustes. La crea el seed: si falta, la base quedó a medio preparar.</summary>
    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The system settings were not found.");
}
