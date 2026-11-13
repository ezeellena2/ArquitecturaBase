using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// El tope diario de plantillas de autenticación (sección 13 del spec del ingreso con WhatsApp), que acota el costo si
/// alguien abusa del formulario con números ajenos. Cuenta los códigos que salieron por teléfono (el canal Phone, que
/// hoy es solo WhatsApp), a cualquier número y con cualquier propósito: se pagan igual. Es global y no por número: le responde igual a todos, así que no sirve para
/// averiguar qué números tienen cuenta. Es aproximado: el lock es por número, así que dos pedidos simultáneos a números
/// distintos pueden pasar con un solo lugar libre y el tope se pasa por unos pocos. Para acotar el costo alcanza. Lo
/// llama <see cref="WhatsAppCodeIssuer"/> antes de emitir, sin esperar a nadie: no se protege con el lock de un número.
/// Para que una sola cuenta o una sola dirección no lo gasten (H10), los códigos del perfil tienen además un tope por cuenta y, con
/// el registro abierto, cada código que sale gasta un lugar de la dirección del pedido; el que lleva el día al 80 % deja un aviso.
/// </summary>
internal sealed partial class WhatsAppCodeQuotaGuard(
    ILoginCodeRepository loginCodes,
    IOptions<WhatsAppLoginOptions> options,
    IRequestInfo request,
    IWhatsAppCodeAddressLimiter addresses,
    TimeProvider timeProvider,
    ILogger<WhatsAppCodeQuotaGuard> logger)
{
    /// <summary>El tope cuenta en una ventana móvil de 24 horas, no por día calendario.</summary>
    private static readonly TimeSpan DailyWindow = TimeSpan.FromDays(1);

    // Los que salieron en la ventana, leídos por CheckAsync en este pedido: el aviso del 80 % sale recién cuando el código sale.
    private int? _sentInWindow;

    /// <summary>
    /// Null si todavía queda lugar; si no, <c>Auth.LoginCode.TooManyRequests</c> con los segundos que faltan para que
    /// el más viejo de la ventana salga de ella.
    /// </summary>
    public async Task<Error?> CheckAsync(CancellationToken cancellationToken)
    {
        var limit = options.Value.DailyAuthCodeLimit;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var sentTimes = await loginCodes.ListLatestSentTimesAsync(
            LoginCodeChannel.Phone, nowUtc - DailyWindow, limit, cancellationToken);
        _sentInWindow = sentTimes.Count;

        if (sentTimes.Count < limit)
        {
            return null;
        }

        LogDailyLimitReached(logger, limit);

        // Vienen del más nuevo al más viejo: se libera un lugar cuando el último de la lista sale de la ventana.
        return LoginCodeErrors.TooManyRequests(LoginCodeIssuer.SecondsUntil(sentTimes[^1] + DailyWindow, nowUtc));
    }

    /// <summary>
    /// Después de que un código salió: el que lleva los del día al 80 % del tope deja un aviso, una vez en el caso común (dos pedidos a
    /// la vez pueden avisar los dos o ninguno: es un aviso).
    /// </summary>
    public void ReportSent()
    {
        var limit = options.Value.DailyAuthCodeLimit;
        if (_sentInWindow is { } sent && sent + 1 == WarningThreshold(limit)) LogDailyLimitNear(logger, sent + 1, limit);
    }

    /// <summary>
    /// Con el registro abierto, un código que salió gasta un lugar de la dirección del pedido. Se toma al final, así un pedido que
    /// rechazan los límites por número o la cola no gasta nada, y un rechazo acá descarta el código con la unidad de trabajo. Sin
    /// dirección conocida no se limita.
    /// </summary>
    public Error? TakeAddress()
    {
        if (request.IpAddress is not { Length: > 0 } address || addresses.TryTake(address) is not { } retryAfter) return null;
        LogAddressLimitReached(logger);
        return LoginCodeErrors.TooManyRequests((int)Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds)));
    }

    /// <summary>Un código para vincular un número desde el perfil de <paramref name="userId"/>: el tope de todos y el de la cuenta.</summary>
    public async Task<Error?> CheckLinkAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await CheckAsync(cancellationToken) is { } error) return error;
        var limit = options.Value.DailyLinkCodeLimitPerAccount;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var sentTimes = await loginCodes.ListLatestSentTimesByRequesterAsync(LoginCodeChannel.Phone, userId, nowUtc - DailyWindow, limit, cancellationToken);
        if (sentTimes.Count < limit) return null;
        LogAccountLimitReached(logger, limit);
        return LoginCodeErrors.TooManyRequests(LoginCodeIssuer.SecondsUntil(sentTimes[^1] + DailyWindow, nowUtc));
    }

    private static int WarningThreshold(int limit) => (int)Math.Ceiling(limit * 0.8m);

    // Sin el número: es el tope de todos, y el que llega a tocarlo no dice nada de los demás.
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The daily limit of WhatsApp codes ({Limit} in 24 hours) was reached; no code is sent until the oldest one leaves the window")]
    private static partial void LogDailyLimitReached(ILogger logger, int limit);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The WhatsApp codes of the last 24 hours reached {Sent} of the daily limit of {Limit}; at the limit no code is sent")]
    private static partial void LogDailyLimitNear(ILogger logger, int sent, int limit);

    // Sin la cuenta ni la dirección: sólo que pasó.
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "An account reached its daily limit of {Limit} WhatsApp codes to link a number; no code is sent to it until the oldest one leaves the window")]
    private static partial void LogAccountLimitReached(ILogger logger, int limit);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "An address reached its daily limit of WhatsApp codes while registration is open; no code is sent for it until its window frees a place")]
    private static partial void LogAddressLimitReached(ILogger logger);
}
