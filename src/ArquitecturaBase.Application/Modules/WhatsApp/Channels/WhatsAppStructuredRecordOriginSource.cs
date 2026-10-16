using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Channels;
internal sealed class WhatsAppStructuredRecordOriginSource(IWhatsAppRecordOriginReader reader, IPermissionService permissions) : IStructuredRecordOriginSource
{
    public async Task<IReadOnlyList<StructuredRecordOrigin>> ListAsync(Guid actorUserId, IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken)
    {
        var canOpen = await permissions.HasPermissionAsync(actorUserId, WhatsAppPermissions.Read, cancellationToken);
        var rows = await reader.ListAsync(sourceReceiptIds, canOpen, cancellationToken);
        if (canOpen && !await permissions.HasPermissionAsync(actorUserId, WhatsAppPermissions.Read, cancellationToken))
            return rows.Select(row => row with { ConversationId = null }).ToArray();
        return rows;
    }
}
