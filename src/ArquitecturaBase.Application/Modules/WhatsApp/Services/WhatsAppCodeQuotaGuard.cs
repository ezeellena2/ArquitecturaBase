using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// El tope diario de plantillas de autenticación (sección 13 del spec del ingreso con WhatsApp), que acota el costo si
/// alguien abusa del formulario con números ajenos. Cuenta los códigos que salieron por WhatsApp, a cualquier número y
/// con cualquier propósito: se pagan igual. Es global y no por número: le responde igual a todos, así que no sirve para
/// averiguar qué números tienen cuenta. Es aproximado: el lock es por número, así que dos pedidos simultáneos a números
/// distintos pueden pasar con un solo lugar libre y el tope se pasa por unos pocos. Para acotar el costo alcanza. Lo
/// llama <see cref="WhatsAppCodeIssuer"/> antes de emitir, sin esperar a nadie: no se protege con el lock de un número.
/// </summary>
internal sealed partial class WhatsAppCodeQuotaGuard(
    ILoginCodeRepository loginCodes,
    IOptions<WhatsAppLoginOptions> options,
    TimeProvider timeProvider,
    ILogger<WhatsAppCodeQuotaGuard> logger)
{
    /// <summary>El tope cuenta en una ventana móvil de 24 horas, no por día calendario.</summary>
    private static readonly TimeSpan DailyWindow = TimeSpan.FromDays(1);

    /// <summary>
    /// Null si todavía queda lugar; si no, <c>Auth.LoginCode.TooManyRequests</c> con los segundos que faltan para que
    /// el más viejo de la ventana salga de ella.
    /// </summary>
    public async Task<Error?> CheckAsync(CancellationToken cancellationToken)
    {
        var limit = options.Value.DailyAuthCodeLimit;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var sentTimes = await loginCodes.ListLatestSentTimesAsync(
            LoginCodeChannel.WhatsApp, nowUtc - DailyWindow, limit, cancellationToken);

        if (sentTimes.Count < limit)
        {
            return null;
        }

        LogDailyLimitReached(logger, limit);

        // Vienen del más nuevo al más viejo: se libera un lugar cuando el último de la lista sale de la ventana.
        return LoginCodeErrors.TooManyRequests(LoginCodeIssuer.SecondsUntil(sentTimes[^1] + DailyWindow, nowUtc));
    }

    // Sin el número: es el tope de todos, y el que llega a tocarlo no dice nada de los demás.
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The daily limit of WhatsApp codes ({Limit} in 24 hours) was reached; no code is sent until the oldest one leaves the window")]
    private static partial void LogDailyLimitReached(ILogger logger, int limit);
}
