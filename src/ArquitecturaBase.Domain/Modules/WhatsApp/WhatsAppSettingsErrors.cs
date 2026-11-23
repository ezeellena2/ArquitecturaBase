using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public static class WhatsAppSettingsErrors
{
    public static readonly Error Invalid = Error.Validation("WhatsApp.Settings.Invalid", "The WhatsApp settings are invalid.");
    public static readonly Error RevisionConflict = Error.Conflict("WhatsApp.Settings.RevisionConflict", "Another save changed the WhatsApp settings first.");
}
