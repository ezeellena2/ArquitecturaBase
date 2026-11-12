using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Who may use the inbox (decision 14), checked with the current permissions on every operation: «Ver documentos recibidos» to see it,
/// «Revisar documentos» to change it, both of an active account; and whether the person who sent a document can still load responses
/// (registering it) or is still active (passing it to the form).
/// </summary>
internal sealed class FormReceivedDocumentAccessGuard(ICurrentUser current, IUserReader users, IPermissionService permissions)
{
    public async Task<Result<UserAccount>> CheckAsync(string permission, CancellationToken ct)
    {
        if (current.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        return await users.FindByIdAsync(actor, ct) is { IsActive: true } account && await permissions.HasPermissionAsync(actor, permission, ct)
            ? account : FormDefinitionErrors.Forbidden;
    }

    public async Task<bool> HasAsync(Guid userId, string permission, CancellationToken ct) =>
        userId != Guid.Empty && await users.FindByIdAsync(userId, ct) is { IsActive: true } && await permissions.HasPermissionAsync(userId, permission, ct);

    /// <summary>The account of whoever sent a document while it is active (its language is the response's), or null.</summary>
    public async Task<UserAccount?> FindActiveAsync(Guid userId, CancellationToken ct) =>
        userId != Guid.Empty && await users.FindByIdAsync(userId, ct) is { IsActive: true } account ? account : null;
}
