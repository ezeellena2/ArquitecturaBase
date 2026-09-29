namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// La parte del módulo WhatsApp de TransactionBoundaryTests: sus claves de lock y su excepción a ExecuteUpdate. Lleva el
/// namespace de la clase del núcleo, no el de su carpeta, para ser otra parte de la misma clase.
/// </summary>
public sealed partial class TransactionBoundaryTests
{
    private const string WhatsAppLockKeys = "ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.WhatsAppLockKeys";

    private const string WhatsAppMessageRetentionRepository =
        "ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories.WhatsAppMessageRetentionRepository";

    static partial void AddModuleLockKeyOwners(List<string> owners) => owners.Add(WhatsAppLockKeys);

    static partial void AddModuleLockKeyPrefixes(List<string> prefixes) =>
        prefixes.AddRange(["whatsapp-contact:user:", "whatsapp-contact:wa:", "whatsapp-message:"]);

    // La retención vacía el texto de los mensajes viejos con ExecuteUpdate: WhatsAppMessage no es IAuditable ni
    // ISoftDeletable, así que no hay interceptor que saltear.
    static partial void AddModuleBulkUpdateOwners(List<string> owners) => owners.Add(WhatsAppMessageRetentionRepository);
}
