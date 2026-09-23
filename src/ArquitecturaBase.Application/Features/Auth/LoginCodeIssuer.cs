using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Auth;

/// <summary>
/// Emite un código para entrar, igual para el correo y para WhatsApp: pone en fila los pedidos del destino, aplica los
/// límites por destino (sección 5.3 del spec y 13 del spec del ingreso con WhatsApp), invalida los códigos para entrar
/// que seguían activos y agrega el nuevo. Mandarlo, y marcarlo como enviado, es de cada caso de uso: el canal y a
/// quién se le manda lo deciden ellos. El límite por IP lo aplica el rate limiter de la Api.
/// </summary>
internal sealed class LoginCodeIssuer(
    ILoginCodeRepository loginCodes,
    ILoginCodeGenerator codeGenerator,
    ILoginCodeHasher codeHasher,
    IOptions<LoginCodeOptions> options,
    TimeProvider timeProvider)
{
    public async Task<Result<IssuedLoginCode>> IssueSignInCodeAsync(
        LoginCodeDestination destination,
        CancellationToken cancellationToken)
    {
        // Los límites se aplican de a un pedido por destino. La hora se toma después del lock: un pedido que esperó
        // a otro ve el código que ese otro acaba de emitir.
        await loginCodes.LockDestinationAsync(destination, cancellationToken);

        var settings = options.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var limitError = await CheckLimitsAsync(destination, settings, nowUtc, cancellationToken);

        if (limitError is not null)
        {
            return limitError;
        }

        foreach (var activeCode in await loginCodes.ListActiveAsync(destination, LoginCodePurpose.SignIn, nowUtc, cancellationToken))
        {
            activeCode.Invalidate(nowUtc);
        }

        var code = codeGenerator.Generate();

        var loginCode = LoginCode.Issue(
            destination,
            LoginCodePurpose.SignIn,
            requestedByUserId: null,
            codeHasher.Hash(destination, LoginCodePurpose.SignIn, code),
            nowUtc,
            TimeSpan.FromMinutes(settings.LifetimeMinutes),
            settings.MaxAttempts);

        loginCodes.Add(loginCode);

        return new IssuedLoginCode(loginCode, code);
    }

    /// <summary>Los segundos que faltan para <paramref name="momentUtc"/>, como mínimo 1: es lo que va en retryAfter.</summary>
    public static int SecondsUntil(DateTime momentUtc, DateTime nowUtc) =>
        Math.Max(1, (int)Math.Ceiling((momentUtc - nowUtc).TotalSeconds));

    /// <summary>
    /// Los dos límites son por destino, con cualquier propósito (sección 6.3 del spec del ingreso con WhatsApp):
    /// protegen a quien recibe los mensajes, así que un código que la cuenta pidió desde el perfil para ese mismo
    /// destino también cuenta. Por eso el reenvío mira el último pedido del destino y no el último código de ingreso.
    /// </summary>
    private async Task<Error?> CheckLimitsAsync(
        LoginCodeDestination destination,
        LoginCodeOptions settings,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var window = TimeSpan.FromMinutes(settings.RequestWindowMinutes);
        var cooldown = TimeSpan.FromSeconds(settings.ResendCooldownSeconds);

        // Una sola consulta para los dos límites, que cubre el más largo de los dos plazos: se configuran por separado.
        var requestTimes = await loginCodes.ListRequestTimesSinceAsync(
            destination, nowUtc - (window > cooldown ? window : cooldown), cancellationToken);

        var windowStartUtc = nowUtc - window;
        var requestTimesInWindow = requestTimes.Where(requestedAtUtc => requestedAtUtc > windowStartUtc).ToList();

        if (requestTimesInWindow.Count >= settings.MaxRequestsPerWindow)
        {
            // Se libera un lugar cuando el pedido más viejo de la ventana sale de ella.
            return LoginCodeErrors.TooManyRequests(SecondsUntil(requestTimesInWindow[0] + window, nowUtc));
        }

        var resendAllowedAtUtc = requestTimes.Count > 0 ? requestTimes[^1] + cooldown : (DateTime?)null;

        return resendAllowedAtUtc > nowUtc
            ? LoginCodeErrors.ResendTooSoon(SecondsUntil(resendAllowedAtUtc.Value, nowUtc))
            : null;
    }
}

/// <summary>
/// El código recién emitido: la fila, ya agregada al repositorio, y el código en claro para mandarlo. Es una clase y
/// no un record a propósito: un record imprime sus propiedades, y el código no tiene que terminar en un log.
/// </summary>
internal sealed class IssuedLoginCode(LoginCode loginCode, string code)
{
    public LoginCode LoginCode { get; } = loginCode;

    /// <summary>El código en claro. Solo se usa para mandarlo; la fila guarda su hash.</summary>
    public string Code { get; } = code;

    /// <summary>Cuándo se emitió: la misma hora con que se marca el envío.</summary>
    public DateTime IssuedAtUtc => LoginCode.CreatedAtUtc;
}
