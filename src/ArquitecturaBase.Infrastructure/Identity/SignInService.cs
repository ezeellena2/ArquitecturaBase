using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// ISignInService sobre UserManager, SignInManager y los managers de OpenIddict. Las escrituras (los intentos fallidos y
/// el cierre de sesiones) exigen la transacción del caso de uso con su propio chequeo, y la cookie de la aplicación exige
/// lo contrario: no se escribe adentro de un límite. La cuenta se carga siempre con
/// <see cref="UserManagerExtensions.RequireUserAsync"/>.
/// </summary>
internal sealed class SignInService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictTokenManager tokenManager,
    ApplicationDbContext dbContext) : ISignInService
{
    public async Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken) =>
        await userManager.IsLockedOutAsync(await userManager.RequireUserAsync(userId, cancellationToken));

    public async Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        (await userManager.AccessFailedAsync(await userManager.RequireUserAsync(userId, cancellationToken)))
            .EnsureSucceeded("register the failed attempt");
    }

    public async Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();

        (await userManager.ResetAccessFailedCountAsync(await userManager.RequireUserAsync(userId, cancellationToken)))
            .EnsureSucceeded("reset the failed attempts");
    }

    public async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // El stamp y las dos revocaciones tienen que ir juntos. Las revocaciones de OpenIddict son UPDATE inmediatos, y
        // sin la transacción del caso de uso se confirmarían sueltas. Los enlaces pendientes los invalida
        // AccountAccessRevoker, que es quien llama.
        dbContext.RequireTransaction();
        var user = await userManager.RequireUserAsync(userId, cancellationToken);

        // La cookie de Identity deja de valer en la próxima petición: el validador del security stamp la rechaza
        // (ValidationInterval está en cero, ver IdentityRegistration).
        (await userManager.UpdateSecurityStampAsync(user)).EnsureSucceeded("renew the security stamp");

        // El subject es el mismo que pone OpenIdPrincipalFactory en el claim "sub".
        var subject = userId.ToString("D", CultureInfo.InvariantCulture);

        // Primero las autorizaciones y después los tokens: si entre las dos llamadas se emitiera un token a partir
        // de una autorización que ya está revocada, la segunda llamada igual lo alcanza. Con
        // EnableTokenEntryValidation, un token revocado deja de valer en el acto, sin esperar a que venza.
        await authorizationManager.RevokeBySubjectAsync(subject, cancellationToken);
        await tokenManager.RevokeBySubjectAsync(subject, cancellationToken);
    }

    public async Task SignInAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Después del commit: con el límite abierto, un commit fallido dejaría una sesión de una cuenta, un código o un
        // enlace que no quedaron guardados. Va antes de tocar el HttpContext.
        dbContext.RequireNoTransaction();

        await signInManager.SignInAsync(await userManager.RequireUserAsync(userId, cancellationToken), isPersistent: true);
    }

    public async Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken)
    {
        var info = await signInManager.GetExternalLoginInfoAsync();

        if (info is null)
        {
            return null;
        }

        return new ExternalLogin(
            info.LoginProvider,
            info.ProviderKey,
            info.Principal.FindFirstValue(ClaimTypes.Email),
            string.Equals(info.Principal.FindFirstValue(ExternalClaimTypes.EmailVerified), "true", StringComparison.OrdinalIgnoreCase),
            info.Principal.FindFirstValue(ClaimTypes.Name));
    }

    public Task SignOutExternalAsync(CancellationToken cancellationToken) =>
        signInManager.Context.SignOutAsync(IdentityConstants.ExternalScheme);
}
