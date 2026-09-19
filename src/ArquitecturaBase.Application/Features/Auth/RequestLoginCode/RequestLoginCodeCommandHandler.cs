using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Emails;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Auth.RequestLoginCode;

/// <summary>
/// Emite un código nuevo y encola el email. Aplica el reenvío y el límite por email (sección 5.3); el límite por IP
/// lo aplica el rate limiter de la Api. El email se encola antes de guardar: si el guardado fallara, el usuario
/// recibiría un código que no sirve y pediría otro.
/// </summary>
internal sealed class RequestLoginCodeCommandHandler(
    ILoginCodeRepository loginCodes,
    IIdentityService identityService,
    ILoginCodeGenerator codeGenerator,
    ILoginCodeHasher codeHasher,
    IEmailTemplateRenderer templateRenderer,
    IEmailQueue emailQueue,
    IOptions<LoginCodeOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<RequestLoginCodeCommand, RequestLoginCodeResponse>
{
    public async Task<Result<RequestLoginCodeResponse>> Handle(RequestLoginCodeCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);

        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;

        // Los límites de la sección 5.3 se aplican de a un request por email.
        await loginCodes.LockEmailAsync(email, cancellationToken);

        var settings = options.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var limitError = await CheckLimitsAsync(email, settings, nowUtc, cancellationToken);

        if (limitError is not null)
        {
            return limitError;
        }

        foreach (var activeCode in await loginCodes.ListActiveAsync(email, nowUtc, cancellationToken))
        {
            activeCode.Invalidate(nowUtc);
        }

        var code = codeGenerator.Generate();

        loginCodes.Add(LoginCode.Issue(
            email,
            codeHasher.Hash(email, code),
            nowUtc,
            TimeSpan.FromMinutes(settings.LifetimeMinutes),
            settings.MaxAttempts));

        // El email sale en el idioma del perfil; si la cuenta todavía no existe, en el de la petición.
        var user = await identityService.FindByEmailAsync(email, cancellationToken);
        var culture = user is null ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(user.Culture);

        await emailQueue.EnqueueAsync(
            templateRenderer.RenderLoginCode(email.Value, code, settings.LifetimeMinutes, culture),
            cancellationToken);

        return new RequestLoginCodeResponse(settings.ResendCooldownSeconds);
    }

    private async Task<Error?> CheckLimitsAsync(Email email, LoginCodeOptions settings, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var window = TimeSpan.FromMinutes(settings.RequestWindowMinutes);
        var requestTimes = await loginCodes.ListRequestTimesSinceAsync(email, nowUtc - window, cancellationToken);

        if (requestTimes.Count >= settings.MaxRequestsPerWindow)
        {
            // Se libera un lugar cuando el pedido más viejo de la ventana sale de ella.
            return LoginCodeErrors.TooManyRequests(SecondsUntil(requestTimes[0] + window, nowUtc));
        }

        var latest = await loginCodes.GetLatestAsync(email, cancellationToken);
        var resendAllowedAtUtc = latest?.CreatedAtUtc + TimeSpan.FromSeconds(settings.ResendCooldownSeconds);

        return resendAllowedAtUtc > nowUtc
            ? LoginCodeErrors.ResendTooSoon(SecondsUntil(resendAllowedAtUtc.Value, nowUtc))
            : null;
    }

    private static int SecondsUntil(DateTime momentUtc, DateTime nowUtc) =>
        Math.Max(1, (int)Math.Ceiling((momentUtc - nowUtc).TotalSeconds));
}
