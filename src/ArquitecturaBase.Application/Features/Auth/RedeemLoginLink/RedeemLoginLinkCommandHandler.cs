using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Auth.RedeemLoginLink;

/// <summary>
/// Canjea el enlace y abre la sesión. La cookie sale en la respuesta de este pedido, el que hace el navegador de la
/// persona al tocar Continuar, nunca en el del webhook (sección 5 del spec del ingreso con WhatsApp). Un enlace que no
/// sirve responde siempre lo mismo; "bloqueada" y "deshabilitada" se dicen recién con un enlace válido (sección 6.4).
/// </summary>
internal sealed class RedeemLoginLinkCommandHandler(
    ILoginLinkRepository loginLinks,
    ILoginAuditRepository loginAudits,
    ISecureTokenGenerator tokens,
    IIdentityService identityService,
    IRequestInfo requestInfo,
    TimeProvider timeProvider)
    : ICommandHandler<RedeemLoginLinkCommand>
{
    public async Task<Result> Handle(RedeemLoginLinkCommand command, CancellationToken cancellationToken)
    {
        var tokenHash = tokens.Hash(command.Token!);

        // Un enlace inventado no es de nadie, y por eso no se audita: la auditoría registra los ingresos de una cuenta,
        // y una fila sin cuenta ni identificador no le dice nada a quien la lea. El intento igual queda en el log del
        // caso de uso, con el código de error y sin el token, y el límite por IP frena a quien pruebe a ciegas, que
        // con 256 bits no tiene nada que encontrar.
        var userId = await loginLinks.FindUserIdAsync(tokenHash, cancellationToken);

        if (userId is null)
        {
            return LoginLinkErrors.Invalid;
        }

        // De a un canje por cuenta, y el enlace se vuelve a leer con el lock tomado: si llegan dos pedidos con el mismo
        // enlace, el segundo ve que el primero ya lo consumió y responde que no sirve. Sin esto, entrarían los dos.
        await loginLinks.LockAccountAsync(userId.Value, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var loginLink = await loginLinks.GetByTokenHashAsync(tokenHash, cancellationToken);

        // Se consume antes de mirar la cuenta, y queda consumido aunque la respuesta sea 403 o 429, porque el comando
        // guarda también cuando falla. Un enlace sirve para un solo intento: si no se consumiera, quien lo tenga podría
        // reintentar durante sus 10 minutos hasta que alguien reactive la cuenta o se levante el bloqueo, y cada
        // intento le volvería a contar el estado de la cuenta. Para probar de nuevo, se le pide otro al bot.
        var redemption = loginLink?.Redeem(nowUtc) ?? Result.Failure(LoginLinkErrors.Invalid);

        // Una cuenta borrada después de emitir el enlace ya no existe para nadie: el enlace es uno más que no sirve.
        var user = await identityService.FindByIdAsync(userId.Value, cancellationToken);

        if (user is null)
        {
            return LoginLinkErrors.Invalid;
        }

        // Vencido, usado o invalidado: el mismo error que un enlace inventado. Este intento sí se audita, porque el
        // enlace es de una cuenta: puede ser alguien probando con un enlace viejo que le reenviaron.
        if (redemption.IsFailure)
        {
            return Fail(user, redemption.Error, nowUtc);
        }

        if (await identityService.IsLockedOutAsync(user.Id, cancellationToken))
        {
            return Fail(user, AccountErrors.LockedOut, nowUtc);
        }

        if (!user.IsActive)
        {
            return Fail(user, AccountErrors.Disabled, nowUtc);
        }

        // Como con el código: un ingreso bueno corta la racha de verificaciones fallidas que bloquea la cuenta.
        await identityService.ResetFailedAttemptsAsync(user.Id, cancellationToken);
        await identityService.SignInAsync(user.Id, cancellationToken);

        loginAudits.Add(LoginAudit.Success(
            IdentifierOf(user), user.Id, LoginMethod.WhatsAppLink, requestInfo.IpAddress, requestInfo.UserAgent, nowUtc));

        return Result.Success();
    }

    /// <summary>
    /// Con qué se presenta la persona en la auditoría: el número en formato internacional, que es a donde llegó el
    /// enlace, o el correo si la cuenta no tiene número (el bot solo le manda enlaces a una cuenta con número, pero la
    /// fila no depende de eso).
    /// </summary>
    private static string IdentifierOf(UserAccount user) =>
        user.PhoneNumber ?? user.Email ?? throw new InvalidOperationException("Every account has an email or a phone number.");

    private Error Fail(UserAccount user, Error error, DateTime nowUtc)
    {
        loginAudits.Add(LoginAudit.Failure(
            IdentifierOf(user), user.Id, LoginMethod.WhatsAppLink, error.Code, requestInfo.IpAddress, requestInfo.UserAgent, nowUtc));

        return error;
    }
}
