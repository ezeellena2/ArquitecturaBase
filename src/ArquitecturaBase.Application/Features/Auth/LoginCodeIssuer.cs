using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Auth;

/// <summary>
/// Emite un código, igual para el correo y para WhatsApp, para entrar o para vincular el destino desde el perfil: pone
/// en fila los pedidos del destino, aplica los límites por destino (sección 5.3 del spec y 13 del spec del ingreso con
/// WhatsApp), invalida los códigos del mismo propósito que seguían activos y agrega el nuevo. Por WhatsApp aplica
/// además el tope diario de plantillas de autenticación, que se pagan sea cual sea el propósito. Mandarlo, y marcarlo
/// como enviado, es de cada caso de uso: el canal y a quién se le manda lo deciden ellos. El límite por IP lo aplica el
/// rate limiter de la Api.
/// </summary>
internal sealed partial class LoginCodeIssuer(
    ILoginCodeRepository loginCodes,
    ILoginCodeGenerator codeGenerator,
    ILoginCodeHasher codeHasher,
    IOptions<LoginCodeOptions> options,
    IOptions<WhatsAppLoginOptions> whatsAppOptions,
    TimeProvider timeProvider,
    ILogger<LoginCodeIssuer> logger)
{
    /// <summary>El tope diario cuenta en una ventana móvil de 24 horas, no por día calendario.</summary>
    private static readonly TimeSpan DailyWindow = TimeSpan.FromDays(1);

    /// <summary>Un código para entrar: todavía no es de ninguna cuenta.</summary>
    public Task<Result<IssuedLoginCode>> IssueSignInCodeAsync(
        LoginCodeDestination destination,
        CancellationToken cancellationToken) =>
        IssueAsync(destination, LoginCodePurpose.SignIn, requestedByUserId: null, cancellationToken);

    /// <summary>
    /// Un código para que <paramref name="userId"/> demuestre desde el perfil que el destino es suyo (sección 12 del spec
    /// del ingreso con WhatsApp). Solo invalida los que pidió esa misma cuenta: si otra pide un código para el mismo
    /// número, no le corta el suyo a quien lo estaba vinculando. Los límites, en cambio, son del destino y se comparten.
    /// </summary>
    public Task<Result<IssuedLoginCode>> IssueVerificationCodeAsync(
        LoginCodeDestination destination,
        Guid userId,
        CancellationToken cancellationToken) =>
        IssueAsync(destination, LoginCodePurpose.VerifyDestination, userId, cancellationToken);

    private async Task<Result<IssuedLoginCode>> IssueAsync(
        LoginCodeDestination destination,
        LoginCodePurpose purpose,
        Guid? requestedByUserId,
        CancellationToken cancellationToken)
    {
        // El tope diario es de todos los números y no se protege con el lock de uno: se mira antes, sin esperar a nadie.
        if (destination.Channel is LoginCodeChannel.WhatsApp && await CheckDailyLimitAsync(cancellationToken) is { } dailyLimitError)
        {
            return dailyLimitError;
        }

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

        foreach (var activeCode in await loginCodes.ListActiveAsync(destination, purpose, requestedByUserId, nowUtc, cancellationToken))
        {
            activeCode.Invalidate(nowUtc);
        }

        var code = codeGenerator.Generate();

        var loginCode = LoginCode.Issue(
            destination,
            purpose,
            requestedByUserId,
            codeHasher.Hash(destination, purpose, code),
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

    /// <summary>
    /// El tope diario de plantillas de autenticación (sección 13 del spec del ingreso con WhatsApp), que acota el costo
    /// si alguien abusa del formulario con números ajenos. Cuenta los códigos que salieron por WhatsApp, a cualquier
    /// número y con cualquier propósito. Es global y no por número: le responde igual a todos, así que no sirve para
    /// averiguar qué números tienen cuenta. Es aproximado: el lock es por número, así que dos pedidos simultáneos a
    /// números distintos pueden pasar con un solo lugar libre y el tope se pasa por unos pocos. Para acotar el costo
    /// alcanza.
    /// </summary>
    private async Task<Error?> CheckDailyLimitAsync(CancellationToken cancellationToken)
    {
        var limit = whatsAppOptions.Value.DailyAuthCodeLimit;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var sentTimes = await loginCodes.ListLatestSentTimesAsync(
            LoginCodeChannel.WhatsApp, nowUtc - DailyWindow, limit, cancellationToken);

        if (sentTimes.Count < limit)
        {
            return null;
        }

        LogDailyLimitReached(logger, limit);

        // Vienen del más nuevo al más viejo: se libera un lugar cuando el último de la lista sale de la ventana.
        return LoginCodeErrors.TooManyRequests(SecondsUntil(sentTimes[^1] + DailyWindow, nowUtc));
    }

    // Sin el número: es el tope de todos, y el que llega a tocarlo no dice nada de los demás.
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The daily limit of WhatsApp codes ({Limit} in 24 hours) was reached; no code is sent until the oldest one leaves the window")]
    private static partial void LogDailyLimitReached(ILogger logger, int limit);
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
