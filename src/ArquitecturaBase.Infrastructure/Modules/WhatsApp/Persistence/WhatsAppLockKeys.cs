namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence;

/// <summary>
/// Las claves de pg_advisory_xact_lock del módulo WhatsApp, con las mismas reglas que las del núcleo
/// (<c>AdvisoryLockKeys</c>): el texto ES el lock, así que cambiar un prefijo deja de poner en fila a quien use el texto
/// viejo, por ejemplo la versión anterior de la Api durante un despliegue. Los contactos se piden antes que la cuenta, y
/// los mensajes después de los contactos, en otra llamada. WhatsAppLockKeysTests fija cada texto.
/// </summary>
internal static class WhatsAppLockKeys
{
    /// <summary>
    /// "whatsapp-contact:user:" + el BSUID. El prefijo hace que el mismo texto como BSUID y como número no compartan lock.
    /// </summary>
    public static string WhatsAppContactByUser(string userIdentifier) => "whatsapp-contact:user:" + userIdentifier;

    /// <summary>"whatsapp-contact:wa:" + el wa_id, tal como lo manda Meta (sin '+').</summary>
    public static string WhatsAppContactByWaId(string waId) => "whatsapp-contact:wa:" + waId;

    /// <summary>"whatsapp-message:" + el wamid. Se pide después de los contactos, en otra llamada.</summary>
    public static string WhatsAppMessage(string waMessageId) => "whatsapp-message:" + waMessageId;
}
