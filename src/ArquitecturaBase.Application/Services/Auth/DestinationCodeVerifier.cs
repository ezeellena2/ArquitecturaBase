using ArquitecturaBase.Application.Interfaces.Integrations.Security;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>
/// Verifica los códigos de una cuenta desde el perfil: el que prueba que un número o un correo es suyo
/// (<see cref="LoginCodePurpose.VerifyDestination"/>, sección 12 del spec del ingreso con WhatsApp) y el que confirma un
/// cambio con un medio que la cuenta ya tiene (<see cref="LoginCodePurpose.ConfirmChange"/>: cada medio confirma los
/// cambios del otro, 2026-11-16). Cada uno sirve sólo para su propósito. Un código equivocado, vencido o usado responde
/// los mismos errores que el ingreso, y cada intento fallido se descuenta del código. No suma a los fallos de la cuenta
/// ni se audita: la persona ya está adentro, y esto no es un ingreso. Quien llama corre dentro de un límite con
/// CommitPolicy.OnAnyResult, así los intentos quedan contados aunque la verificación falle.
/// </summary>
internal sealed class DestinationCodeVerifier(
    ILoginCodeRepository loginCodes,
    ILoginCodeHasher codeHasher,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Toma el lock del destino, el mismo que toman la revisión, la emisión y el ingreso con código. Lo usa quien tiene
    /// que tomarlo antes que otros (el orden único: correo, número, participantes y cuenta) y revisa el código después.
    /// Tomarlo otra vez en la misma transacción no espera.
    /// </summary>
    public Task LockAsync(LoginCodeDestination destination, CancellationToken cancellationToken) =>
        loginCodes.LockDestinationAsync(destination, cancellationToken);

    /// <summary>
    /// Verifica el último código de <paramref name="purpose"/> que pidió <paramref name="userId"/> para
    /// <paramref name="destination"/> y, si es el correcto, lo gasta. Un código de ingreso, de otro propósito o que pidió
    /// otra cuenta recibe la misma respuesta que la falta de código y no gasta intentos. Toma el lock del destino, que
    /// dura lo que la transacción del caso de uso: con él, dos cuentas que confirman el mismo destino a la vez pasan de a
    /// una, y la segunda ya ve lo que guardó la primera.
    /// </summary>
    public async Task<Result> VerifyAsync(
        LoginCodeDestination destination,
        LoginCodePurpose purpose,
        Guid userId,
        string code,
        CancellationToken cancellationToken)
    {
        var check = await CheckAsync(destination, purpose, userId, code, cancellationToken);
        if (check.IsFailure)
        {
            return check.Error;
        }

        Consume(check.Value);
        return Result.Success();
    }

    /// <summary>
    /// Como <see cref="VerifyAsync"/>, con el mismo lock y los mismos errores, pero un código correcto no se gasta: se
    /// devuelve para gastarlo con <see cref="Consume"/> cuando el cambio tiene todos sus códigos (agregar un correo pide
    /// también el del número que la cuenta ya tiene, y vincular un número, el del correo). Uno equivocado descuenta el
    /// intento igual.
    /// </summary>
    public async Task<Result<CheckedCode>> CheckAsync(
        LoginCodeDestination destination,
        LoginCodePurpose purpose,
        Guid userId,
        string code,
        CancellationToken cancellationToken)
    {
        if (purpose is not (LoginCodePurpose.VerifyDestination or LoginCodePurpose.ConfirmChange))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Only an account's own codes are checked here.");
        }

        await loginCodes.LockDestinationAsync(destination, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var loginCode = await loginCodes.GetLatestAsync(destination, purpose, userId, cancellationToken);
        if (loginCode is null)
        {
            return LoginCodeErrors.Invalid(attemptsLeft: null);
        }

        var check = loginCode.CheckFor(userId, codeHasher.Hash(destination, purpose, code), nowUtc);
        return check.IsFailure ? check.Error : new CheckedCode(loginCode, nowUtc);
    }

    /// <summary>
    /// Gasta un código que <see cref="CheckAsync"/> dio por bueno, en la misma transacción y con la hora de esa revisión:
    /// uno que vence entre revisar y gastar se gasta igual, en vez de terminar en un 500.
    /// </summary>
    public static void Consume(CheckedCode checkedCode)
    {
        ArgumentNullException.ThrowIfNull(checkedCode);

        checkedCode.LoginCode.Consume(checkedCode.CheckedAtUtc);
    }
}

/// <summary>Un código que <see cref="DestinationCodeVerifier.CheckAsync"/> dio por bueno, y cuándo.</summary>
internal sealed class CheckedCode(LoginCode loginCode, DateTime checkedAtUtc)
{
    public LoginCode LoginCode { get; } = loginCode;

    /// <summary>La hora de la revisión: con ella se gasta.</summary>
    public DateTime CheckedAtUtc { get; } = checkedAtUtc;
}
