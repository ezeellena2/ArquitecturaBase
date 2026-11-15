using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormRuleAccessGuard(ICurrentUser currentUser, IPermissionService permissions, IEnumerable<IStructuredRecordIssuer> issuers)
{
    public async Task<Result<Guid>> CheckAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        return await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RulesManage, cancellationToken) ? actor : FormDefinitionErrors.Forbidden;
    }
    public async Task<bool> StillAllowedAsync(Guid actor, CancellationToken cancellationToken) => currentUser.UserId == actor
        && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RulesManage, cancellationToken);

    /// <summary>
    /// H6: rules that issue a document leave the form issuing with legal effect, so whoever adds or saves them needs the permission
    /// of each issuer they name too, as the reversal does. Forms never names the module: the permission comes from the issuer. The
    /// missing permission has its own code, so the rule editor tells it instead of taking it as the rules permission lost.
    /// </summary>
    public async Task<Result> MayIssueAsync(Guid actor, IEnumerable<string?> issuerIds, CancellationToken cancellationToken)
    {
        foreach (var issuerId in issuerIds.OfType<string>().Distinct(StringComparer.Ordinal))
        {
            if (issuers.FirstOrDefault(issuer => issuer.IssuerId == issuerId) is not { } issuer) return FormActionErrors.IssuerUnavailable;
            if (!await permissions.HasPermissionAsync(actor, issuer.ReviewPermission, cancellationToken)) return FormRuleErrors.IssuerPermissionRequired;
        }
        return Result.Success();
    }
}
