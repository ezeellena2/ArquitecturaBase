using ArquitecturaBase.ArchitectureTests.Support;
namespace ArquitecturaBase.ArchitectureTests.Modules.WhatsApp;

/// <summary>
/// Aporte del módulo WhatsApp a TransactionBoundaryTests: claves de lock y excepción justificada a ExecuteUpdate.
/// </summary>
internal sealed class WhatsAppTransactionContribution : IModuleArchitectureContribution
{
    private const string WhatsAppLockKeys = "ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.WhatsAppLockKeys";

    private const string WhatsAppMessageRetentionRepository =
        "ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories.WhatsAppMessageRetentionRepository";

    public IReadOnlyCollection<string> LockOwners => [WhatsAppLockKeys];

    public IReadOnlyCollection<string> LockPrefixes => ["whatsapp-contact:user:", "whatsapp-contact:wa:", "whatsapp-message:"];

    // La retención vacía el texto de los mensajes viejos con ExecuteUpdate: WhatsAppMessage no es IAuditable ni
    // ISoftDeletable, así que no hay interceptor que saltear.
    public IReadOnlyCollection<string> BulkUpdateOwners => [WhatsAppMessageRetentionRepository];
}
