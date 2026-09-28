using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Security;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>
/// Verifica el enlace de ingreso que el bot mandó al chat: la vista previa (a quién pertenece, sin gastarlo) y el canje,
/// que devuelve el Id de la cuenta que entra. El canje corre dentro del límite de <see cref="LoginLinkService"/>, que
/// escribe la cookie después del commit. Del token se busca solo su hash.
/// </summary>
internal sealed class LoginLinkVerifier(
    ILoginLinkRepository loginLinks,
    ISecureTokenGenerator tokens,
    IUserReader users,
    ISignInService signIn,
    LoginAuditRecorder audits,
    TimeProvider timeProvider)
{
    /// <summary>La cuenta de un enlace activo, o <see cref="LoginLinkErrors.Invalid"/>. No gasta el enlace ni deja auditoría.</summary>
    public async Task<Result<UserAccount>> PreviewAsync(PreviewLoginLinkRequest request, CancellationToken cancellationToken)
    {
        var loginLink = await loginLinks.GetByTokenHashAsync(tokens.Hash(request.Token!), cancellationToken);
        if (loginLink is null || !loginLink.IsActive(timeProvider.GetUtcNow().UtcDateTime))
        {
            return LoginLinkErrors.Invalid;
        }

        // Una cuenta borrada después de emitir el enlace no puede revelar sus datos en la vista previa.
        var user = await users.FindByIdAsync(loginLink.UserId, cancellationToken);
        if (user is null)
        {
            return LoginLinkErrors.Invalid;
        }

        return user;
    }

    /// <summary>El canje, ya validado: corre dentro del límite de LoginLinkService.RedeemAsync.</summary>
    public async Task<Result<Guid>> RedeemAsync(RedeemLoginLinkRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = tokens.Hash(request.Token!);

        // Un enlace inventado no pertenece a ninguna cuenta y no genera fila de auditoría.
        var userId = await loginLinks.FindUserIdAsync(tokenHash, cancellationToken);
        if (userId is null)
        {
            return LoginLinkErrors.Invalid;
        }

        // Se toma el lock por cuenta y luego se vuelve a leer: solo un canje puede consumir el enlace.
        await loginLinks.LockAccountAsync(userId.Value, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var loginLink = await loginLinks.GetByTokenHashAsync(tokenHash, cancellationToken);
        var redemption = loginLink?.Redeem(nowUtc) ?? Result.Failure(LoginLinkErrors.Invalid);

        var user = await users.FindByIdAsync(userId.Value, cancellationToken);
        if (user is null)
        {
            return LoginLinkErrors.Invalid;
        }

        if (redemption.IsFailure)
        {
            return Fail(user, redemption.Error);
        }

        if (await signIn.IsLockedOutAsync(user.Id, cancellationToken))
        {
            return Fail(user, AccountErrors.LockedOut);
        }

        if (!user.IsActive)
        {
            return Fail(user, AccountErrors.Disabled);
        }

        await signIn.ResetFailedAttemptsAsync(user.Id, cancellationToken);

        audits.Succeeded(IdentifierOf(user), user.Id, LoginMethod.WhatsAppLink);

        return user.Id;
    }

    private static string IdentifierOf(UserAccount user) =>
        user.PhoneNumber ?? user.Email ?? throw new InvalidOperationException("Every account has an email or a phone number.");

    private Error Fail(UserAccount user, Error error) =>
        audits.Failed(IdentifierOf(user), user.Id, LoginMethod.WhatsAppLink, error);
}
