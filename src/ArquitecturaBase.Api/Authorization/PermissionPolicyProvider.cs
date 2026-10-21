using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.Authorization;

/// <summary>Arma al vuelo las políticas "permission:&lt;permiso&gt;";
/// el resto las resuelve el proveedor por defecto.</summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string PolicyPrefix = "permission:";
    public const string AnyPolicyPrefix = PolicyPrefix + "any:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        var permissions = policyName.StartsWith(AnyPolicyPrefix, StringComparison.Ordinal)
            ? policyName[AnyPolicyPrefix.Length..].Split(',')
            : [policyName[PolicyPrefix.Length..]];

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permissions))
            .Build();
    }
}
