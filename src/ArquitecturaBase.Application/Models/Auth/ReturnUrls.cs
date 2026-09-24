using System.Diagnostics.CodeAnalysis;

namespace ArquitecturaBase.Application.Models.Auth;

/// <summary>
/// El returnUrl de un ingreso tiene que ser una ruta local al endpoint de autorización, para evitar redirecciones
/// abiertas (sección 5.2 del spec).
/// </summary>
public static class ReturnUrls
{
    public const string AuthorizePath = "/connect/authorize";

    /// <summary>Página de login del SPA: el backend manda ahí cuando falta la sesión o falla un ingreso externo.</summary>
    public const string LoginPath = "/login";

    public static bool IsAuthorizeRequest([NotNullWhen(true)] string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl.StartsWith(AuthorizePath, StringComparison.Ordinal)
        && (returnUrl.Length == AuthorizePath.Length || returnUrl[AuthorizePath.Length] == '?')
        && !returnUrl.Any(char.IsControl);
}
