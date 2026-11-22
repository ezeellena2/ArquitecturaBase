using ArquitecturaBase.Application.Interfaces.Persistence;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// RC6-B: el nombre del remitente de Configuración › Apariencia, leído antes de armar cada correo. Un remitente no lanza
/// (devuelve un resultado clasificado): si los ajustes no se pueden leer, no salió nada y el correo se vuelve a probar.
/// </summary>
internal static class EmailSenderName
{
    public static async Task<string?> FindAsync(ISystemSettingsReader settingsReader, CancellationToken cancellationToken)
    {
        try
        {
            return (await settingsReader.FindBrandingAsync(cancellationToken)).EmailSenderName;
        }
        catch (Exception exception) when (exception is not ObjectDisposedException)
        {
            return null;
        }
    }
}
