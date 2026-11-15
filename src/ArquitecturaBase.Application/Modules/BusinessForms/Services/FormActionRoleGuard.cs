using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// What the dispatch of an action asks about a role when it reserves: whether it still exists, and who is in it now (active accounts).
/// A notice goes to who is in the role when it is sent (decision 4), and an approval is not asked of a role nobody in can decide (B3).
/// </summary>
internal sealed class FormActionRoleGuard(IFormRuleReader roles, IFormActionRecipientReader recipients)
{
    public async Task<bool> ExistsAsync(Guid roleId, CancellationToken cancellationToken) =>
        (await roles.ListRolesAsync(cancellationToken)).Any(role => role.Id == roleId);

    public async Task<IReadOnlySet<Guid>> MembersAsync(Guid roleId, CancellationToken cancellationToken) =>
        (await recipients.ListForRoleAsync(roleId, cancellationToken)).Select(member => member.UserId).ToHashSet();
}
