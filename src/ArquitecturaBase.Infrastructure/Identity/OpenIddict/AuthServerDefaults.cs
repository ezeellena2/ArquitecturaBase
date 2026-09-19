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
}
