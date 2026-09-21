namespace ArquitecturaBase.Api.Authorization;

internal static class EndpointExtensions
{
    /// <summary>Exige un permiso del catálogo (<c>Domain/Authorization/Permissions</c>).
    /// Los endpoints nunca piden roles.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + permission);
}
