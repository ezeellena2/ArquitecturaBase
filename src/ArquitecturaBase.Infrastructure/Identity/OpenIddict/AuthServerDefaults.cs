namespace ArquitecturaBase.Infrastructure.Identity.OpenIddict;

/// <summary>Cliente, scope y rutas del servidor de autorización (sección 5.5).</summary>
internal static class AuthServerDefaults
{
    public const string WebClientId = "web";
    public const string ApiScope = "api";
    public const string ApiResource = "arquitecturabase-api";

    public const string AuthorizationEndpoint = "connect/authorize";
    public const string TokenEndpoint = "connect/token";
    public const string EndSessionEndpoint = "connect/logout";
    public const string UserInfoEndpoint = "connect/userinfo";
    public const string RevocationEndpoint = "connect/revoke";
    public const string IntrospectionEndpoint = "connect/introspect";

    /// <summary>
    /// Lo que dura un refresh token, el token más largo: lo usan el registro y la poda de la retención (N16), que corre el
    /// umbral de OpenIddict lo que dura un token para no podar nada que no haya vencido antes del plazo.
    /// </summary>
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
}
