using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>
/// Emite el enlace de ingreso de una cuenta, el que el bot manda al chat (secciones 5 y 6.4 del spec del ingreso con
/// WhatsApp): pone en fila las emisiones de la cuenta, aplica los límites (uno por minuto y 5 cada 15 minutos,
/// sección 13), invalida los enlaces que seguían activos y agrega el nuevo. Quién recibe el enlace y cómo se le manda lo
/// decide quien llama; los cambios los guarda su unidad de trabajo. El proyecto de los tests de integración lo ve por
/// <c>InternalsVisibleTo</c> y lo usa para emitir enlaces sin pasar por el bot.
/// </summary>
internal sealed class LoginLinkIssuer(
    ILoginLinkRepository loginLinks,
    ISecureTokenGenerator tokens,
    IPublicOrigin publicOrigin,
    IOptions<LoginLinkOptions> options,
    TimeProvider timeProvider)
{
    /// <summary>La pantalla del SPA que recibe el enlace (sección 11 del spec del ingreso con WhatsApp).</summary>
    public const string PagePath = "ingresar";

    /// <summary>El nombre del token en el fragmento: <c>/ingresar#t=…</c>.</summary>
    public const string TokenParameter = "t";

    public async Task<Result<IssuedLoginLink>> IssueAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Sin el origen público no hay a dónde mandar a la persona. Es un error de configuración, no algo que ella
        // pueda resolver, y se nota antes de tocar nada.
        var origin = publicOrigin.Value
            ?? throw new InvalidOperationException(
                "Authentication:Issuer must be set to the public origin of the web app to build login links.");

        // Los límites se aplican de a una emisión por cuenta. La hora se toma después del lock: una emisión que esperó
        // a otra ve el enlace que esa otra acaba de emitir.
        await loginLinks.LockAccountAsync(userId, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var limitError = await CheckLimitsAsync(userId, options.Value, nowUtc, cancellationToken);

        if (limitError is not null)
        {
            return limitError;
        }

        foreach (var activeLink in await loginLinks.ListActiveAsync(userId, nowUtc, cancellationToken))
        {
            activeLink.Invalidate(nowUtc);
        }

        var token = tokens.Generate();
        var loginLink = LoginLink.Issue(userId, tokens.Hash(token), nowUtc);

        loginLinks.Add(loginLink);

        return new IssuedLoginLink(UrlOf(origin, token), loginLink.ExpiresAtUtc);
    }

    /// <summary>
    /// El token va en el fragmento, que el navegador no le manda al servidor: no queda en los logs, ni en el historial
    /// del servidor ni en el Referer (sección 5 del spec del ingreso con WhatsApp). Es base64url, así que no hace falta
    /// escaparlo. La ruta es relativa al origen, que termina en "/": si la web vive en una subcarpeta, la conserva.
    /// </summary>
    private static string UrlOf(Uri origin, string token) =>
        $"{new Uri(origin, PagePath).AbsoluteUri}#{TokenParameter}={token}";

    /// <summary>
    /// Los dos límites en un solo error, con lo que falta para que se cumplan los dos: la persona no tiene por qué
    /// saber cuál de los dos la frenó.
    /// </summary>
    private async Task<Error?> CheckLimitsAsync(Guid userId, LoginLinkOptions settings, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var window = TimeSpan.FromMinutes(settings.RequestWindowMinutes);
        var cooldown = TimeSpan.FromSeconds(settings.ResendCooldownSeconds);

        // Una sola consulta para los dos límites, que cubre el más largo de los dos plazos: se configuran por separado.
        var issueTimes = await loginLinks.ListIssueTimesSinceAsync(
            userId, nowUtc - (window > cooldown ? window : cooldown), cancellationToken);

        if (issueTimes.Count == 0)
        {
            return null;
        }

        var windowStartUtc = nowUtc - window;
        var issueTimesInWindow = issueTimes.Where(issuedAtUtc => issuedAtUtc > windowStartUtc).ToList();

        // Se libera un lugar cuando sale de la ventana el que deja la cuenta justo debajo del máximo: el más viejo, si
        // nadie bajó el máximo después de emitir.
        var allowedAtUtc = issueTimes[^1] + cooldown;
        var excess = issueTimesInWindow.Count - settings.MaxRequestsPerWindow;

        if (excess >= 0 && issueTimesInWindow[excess] + window > allowedAtUtc)
        {
            allowedAtUtc = issueTimesInWindow[excess] + window;
        }

        return allowedAtUtc > nowUtc
            ? LoginLinkErrors.TooManyRequests(LoginCodeIssuer.SecondsUntil(allowedAtUtc, nowUtc))
            : null;
    }
}

/// <summary>
/// El enlace recién emitido, para mandarlo. Es una clase y no un record a propósito: un record imprime sus
/// propiedades, y la URL lleva el token.
/// </summary>
internal sealed class IssuedLoginLink(string url, DateTime expiresAtUtc)
{
    /// <summary>La dirección completa, con el token en el fragmento. Nunca va a un log ni al historial del chat.</summary>
    public string Url { get; } = url;

    public DateTime ExpiresAtUtc { get; } = expiresAtUtc;
}
