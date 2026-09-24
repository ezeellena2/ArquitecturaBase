using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Users;

/// <summary>
/// Verifica el código con que una cuenta prueba, desde el perfil, que un número o un correo es suyo (sección 12 del spec
/// del ingreso con WhatsApp). Lo comparten vincular el número y agregar el correo. Un código equivocado, vencido o usado
/// responde los mismos errores que el ingreso, y cada intento fallido se descuenta del código. No suma a los fallos de la
/// cuenta ni se audita: la persona ya está adentro, y esto no es un ingreso. Quien llama guarda también cuando falla
/// (<c>IPersistChangesOnFailure</c>), así los intentos quedan contados.
/// </summary>
internal sealed class DestinationCodeVerifier(
    ILoginCodeRepository loginCodes,
    ILoginCodeHasher codeHasher,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Verifica el último código que pidió <paramref name="userId"/> para <paramref name="destination"/> y, si es el
    /// correcto, lo gasta. Un código de ingreso, o uno que pidió otra cuenta, recibe la misma respuesta que la falta de
    /// código y no gasta intentos. Toma el lock del destino, que dura hasta el guardado: con él, dos cuentas que confirman
    /// el mismo destino a la vez pasan de a una, y la segunda ya ve lo que guardó la primera.
    /// </summary>
    public async Task<Result> VerifyAsync(
        LoginCodeDestination destination,
        Guid userId,
        string code,
        CancellationToken cancellationToken)
    {
        await loginCodes.LockDestinationAsync(destination, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var loginCode = await loginCodes.GetLatestAsync(
            destination, LoginCodePurpose.VerifyDestination, userId, cancellationToken);

        return loginCode?.VerifyFor(userId, codeHasher.Hash(destination, LoginCodePurpose.VerifyDestination, code), nowUtc)
            ?? Result.Failure(LoginCodeErrors.Invalid(attemptsLeft: null));
    }
}
