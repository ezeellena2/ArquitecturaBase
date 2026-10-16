using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>Revalidates the real operator of manual text and Flow sends. System actions do not use this guard.</summary>
internal sealed class WhatsAppManualSendGuard(IUserReader users, IPermissionService permissions)
{
    public async Task<bool> IsAllowedAsync(Guid? operatorUserId, CancellationToken cancellationToken)
    {
        if (operatorUserId is not { } actor) return false;
        var account = await users.FindByIdAsync(actor, cancellationToken);
        if (account is not { IsActive: true }) return false;
        var allowed = await permissions.GetPermissionsAsync(actor, cancellationToken);
        return allowed.Contains(WhatsAppPermissions.Read) && allowed.Contains(WhatsAppPermissions.Send);
    }
}
