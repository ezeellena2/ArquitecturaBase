using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// Los dos pedidos de un código por WhatsApp, ya validados: para entrar y para que una cuenta pruebe desde el perfil que
/// un número es suyo (sección 12 del spec del ingreso con WhatsApp). Pasa el número por el país y por el tope diario
/// (<see cref="WhatsAppCodeQuotaGuard"/>), emite el código con <see cref="LoginCodeIssuer"/>, que sigue siendo el único
/// que emite y aplica los límites por destino, y lo encola. Lo que encola queda marcado en la fila antes del commit: corre
/// dentro del límite de quien llama (<see cref="WhatsAppLoginCodeService"/> o <see cref="ProfileWhatsAppService"/>), con
/// OnSuccess, y no guarda.
/// </summary>
internal sealed class WhatsAppCodeIssuer(
    LoginCodeIssuer issuer,
    WhatsAppCodeQuotaGuard quota,
    IUserReader users,
    IPhoneNumberParser phoneNumbers,
    IWhatsAppSendQueue sendQueue,
    IWhatsAppAvailability whatsApp,
    AccountCreationPolicy accountCreation,
    IOptions<WhatsAppLoginOptions> options)
{
    /// <summary>
    /// La ruta HTTP se omite con WhatsApp apagado: llegar hasta acá es un error de programación. El punto de entrada lo
    /// llama después de validar y antes de abrir el límite: un error de configuración no abre transacción.
    /// </summary>
    public void EnsureEnabledForSignIn()
    {
        if (!whatsApp.IsEnabled)
        {
            throw new InvalidOperationException("WhatsApp is disabled (no WhatsApp:PhoneNumberId): no WhatsApp sign-in code can be requested.");
        }
    }

    /// <summary>
    /// Lo mismo para el perfil: impide emitir un código sin entrega si otro consumidor llama al servicio directamente.
    /// </summary>
    public void EnsureEnabledForLink()
    {
        if (!whatsApp.IsEnabled)
        {
            throw new InvalidOperationException("WhatsApp is disabled (no WhatsApp:PhoneNumberId): no code to link a number can be requested.");
        }
    }

    /// <summary>
    /// Un código para entrar, a la cuenta del número o a quien todavía puede crear una. La fila también se guarda para
    /// un número desconocido en InviteOnly: sostiene los mismos límites por destino y no revela si existe la cuenta.
    /// </summary>
    public async Task<Result<RequestWhatsAppLoginCodeResponse>> RequestSignInCodeAsync(
        RequestWhatsAppLoginCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var phoneResult = ReadPhone(request.Country, request.Number);
        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var phone = phoneResult.Value;
        if (await quota.CheckAsync(cancellationToken) is { } quotaError)
        {
            return quotaError;
        }

        var issued = await issuer.IssueSignInCodeAsync(LoginCodeDestination.ForPhone(phone), cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        var user = await users.FindByPhoneAsync(phone, cancellationToken);

        if (user is not null || await accountCreation.AllowsNewAccountAsync(email: null, cancellationToken))
        {
            // La cola puede rechazar el mensaje. En ese caso se guarda el código como no enviado.
            Send(phone, UserCultures.Of(user), issued.Value);
        }

        return new RequestWhatsAppLoginCodeResponse(issued.Value.ResendCooldownSeconds, phone.Value, phoneNumbers.Mask(phone));
    }

    /// <summary>
    /// Un código para que <paramref name="user"/> vincule el número desde el perfil. Comparte límites por destino y tope
    /// diario con los códigos de ingreso.
    /// </summary>
    public async Task<Result<RequestPhoneLinkCodeResponse>> RequestVerificationCodeAsync(
        UserAccount user, RequestPhoneLinkCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);

        var phoneResult = ReadPhone(request.Country, request.Number);
        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var phone = phoneResult.Value;
        if (await quota.CheckAsync(cancellationToken) is { } quotaError)
        {
            return quotaError;
        }

        var issued = await issuer.IssueVerificationCodeAsync(LoginCodeDestination.ForPhone(phone), user.Id, cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        // Si la cola no lo toma, queda sin fecha de envío y no consume el tope.
        Send(phone, UserCultures.Of(user), issued.Value);

        return new RequestPhoneLinkCodeResponse(issued.Value.ResendCooldownSeconds, phone.Value, phoneNumbers.Mask(phone));
    }

    // El país sale del número ya interpretado, no del que eligió la persona.
    private Result<PhoneNumber> ReadPhone(string? country, string? number)
    {
        var phoneResult = phoneNumbers.Parse(country, number);
        if (phoneResult.IsFailure)
        {
            return phoneResult;
        }

        return options.Value.AllowsCountry(phoneNumbers.RegionOf(phoneResult.Value))
            ? phoneResult
            : WhatsAppErrors.CountryNotSupported;
    }

    // Se encola antes del commit para que la fila se confirme ya marcada como enviada. Encolar no espera a Meta, así que
    // no alarga el lock del número.
    private void Send(PhoneNumber phone, string culture, IssuedLoginCode issued)
    {
        if (sendQueue.TryEnqueue(new WhatsAppLoginCodeMessage(phone, culture, issued.Code)))
        {
            issued.LoginCode.MarkSent(issued.IssuedAtUtc);
        }
    }
}
