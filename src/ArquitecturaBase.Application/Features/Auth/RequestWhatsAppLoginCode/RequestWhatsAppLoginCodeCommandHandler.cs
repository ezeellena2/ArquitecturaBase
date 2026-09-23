using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Phones;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Auth.RequestWhatsAppLoginCode;

/// <summary>
/// Emite un código para entrar con WhatsApp y encola la plantilla de autenticación (sección 10 del spec del ingreso
/// con WhatsApp). Las reglas son las del correo: el lock, el reenvío y el límite por número los aplica
/// <see cref="LoginCodeIssuer"/>, y en InviteOnly un número sin cuenta recorre el mismo camino sin que se le mande
/// nada. Suma dos controles propios, porque cada mensaje se paga: el país del número y un tope diario.
/// </summary>
internal sealed partial class RequestWhatsAppLoginCodeCommandHandler(
    LoginCodeIssuer issuer,
    ILoginCodeRepository loginCodes,
    IIdentityService identityService,
    IPhoneNumberParser phoneNumbers,
    IWhatsAppAvailability whatsApp,
    IWhatsAppOutbox outbox,
    AccountCreationPolicy accountCreation,
    IOptions<WhatsAppLoginOptions> whatsAppOptions,
    IOptions<LoginCodeOptions> options,
    TimeProvider timeProvider,
    ILogger<RequestWhatsAppLoginCodeCommandHandler> logger)
    : ICommandHandler<RequestWhatsAppLoginCodeCommand, RequestWhatsAppLoginCodeResponse>
{
    /// <summary>El tope diario cuenta en una ventana móvil de 24 horas, no por día calendario.</summary>
    private static readonly TimeSpan DailyWindow = TimeSpan.FromDays(1);

    public async Task<Result<RequestWhatsAppLoginCodeResponse>> Handle(
        RequestWhatsAppLoginCodeCommand command,
        CancellationToken cancellationToken)
    {
        // Con WhatsApp apagado el endpoint ni se mapea, así que acá no se llega. Si se llegara, se emitiría un código
        // que nunca sale: es un error de programación, no algo que decida quien pide.
        if (!whatsApp.IsEnabled)
        {
            throw new InvalidOperationException("WhatsApp is disabled (no WhatsApp:PhoneNumberId): no WhatsApp sign-in code can be requested.");
        }

        var phoneResult = phoneNumbers.Parse(command.Country, command.Number);

        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var phone = phoneResult.Value;

        // El país es el del número y no el elegido: "+598…" con Argentina elegida sigue siendo un número de Uruguay.
        var region = phoneNumbers.RegionOf(phone);

        if (region is null || !whatsAppOptions.Value.Countries.Contains(region, StringComparer.Ordinal))
        {
            return WhatsAppErrors.CountryNotSupported;
        }

        var dailyLimitError = await CheckDailyLimitAsync(cancellationToken);

        if (dailyLimitError is not null)
        {
            return dailyLimitError;
        }

        var issued = await issuer.IssueSignInCodeAsync(LoginCodeDestination.ForPhone(phone), cancellationToken);

        if (issued.IsFailure)
        {
            return issued.Error;
        }

        var user = await identityService.FindByPhoneAsync(phone, cancellationToken);

        // Como con el correo: en InviteOnly, a un número sin cuenta (o de una cuenta borrada) se le emitió el código
        // pero no se le manda nada, y la respuesta es la misma. La fila se guarda a propósito: los límites por número
        // se apoyan en ella, y sin ella insistir con un número desconocido respondería 202 para siempre mientras uno
        // registrado empieza a responder 429, que alcanza para averiguar qué números tienen cuenta. Sin correo, la
        // excepción del administrador inicial no aplica: el número solo recibe el código si ya tiene cuenta o en Open.
        if (user is not null || await accountCreation.AllowsNewAccountAsync(email: null, cancellationToken))
        {
            var message = new WhatsAppLoginCodeMessage(phone, TemplateLanguageOf(user), issued.Value.Code);

            // Si la cola no lo toma, el código queda sin fecha de envío: no cuenta para el tope y la persona pide otro.
            if (outbox.TryEnqueue(message))
            {
                issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
            }
        }

        return new RequestWhatsAppLoginCodeResponse(options.Value.ResendCooldownSeconds, phone.Value, phoneNumbers.Mask(phone));
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
        return LoginCodeErrors.TooManyRequests(LoginCodeIssuer.SecondsUntil(sentTimes[^1] + DailyWindow, nowUtc));
    }

    /// <summary>
    /// La plantilla existe en "es" y en "en", los mismos códigos que la cultura del perfil: el idioma de la cuenta o,
    /// si todavía no existe, el de la petición.
    /// </summary>
    private static string TemplateLanguageOf(UserAccount? user) =>
        user is not null && UserCultures.IsSupported(user.Culture) ? user.Culture : UserCultures.FromCurrentRequest();

    // Sin el número: es el tope de todos, y el que llega a tocarlo no dice nada de los demás.
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The daily limit of WhatsApp sign-in codes ({Limit} in 24 hours) was reached; no code is sent until the oldest one leaves the window")]
    private static partial void LogDailyLimitReached(ILogger logger, int limit);
}
