using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.WhatsApp;

/// <summary>
/// Lo que el bot escribe para una cuenta (filas 1, 4 y 7 de la sección 8 del spec del ingreso con WhatsApp): el enlace
/// de ingreso de una cuenta activa y el alta de una cuenta con «Crear cuenta». Lo usa <see cref="WhatsAppReplyPolicy"/>,
/// con el contacto y la cuenta ya tomados con su lock por <see cref="WhatsAppInboundService"/> y por la política. No
/// abre ni confirma transacciones: corre adentro del límite del bot.
/// </summary>
internal sealed partial class WhatsAppLinkIssuer(
    LoginLinkIssuer loginLinks,
    WhatsAppContactLinker contactLinker,
    IUserRepository userRepository,
    IAppName appName,
    ILogger<WhatsAppLinkIssuer> logger)
{
    /// <summary>
    /// La primera fila: emite un enlace, vincula el contacto a la cuenta y, si el número de la cuenta es el del chat y
    /// estaba sin verificar (lo cargó un administrador), lo verifica: la firma de Meta prueba que la persona escribe
    /// desde ese número (sección 4 del spec). Si pidió un enlace hace muy poco, no cambia nada.
    /// </summary>
    public async Task<WhatsAppOutboundMessage> SendLoginLinkAsync(
        WhatsAppContact contact,
        PhoneNumber phone,
        UserAccount account,
        CancellationToken cancellationToken)
    {
        var reply = Reply(account);

        // El lock de la cuenta ya lo tiene desde WhatsAppReplyPolicy: el emisor lo vuelve a pedir y pasa de largo.
        var issued = await loginLinks.IssueAsync(account.Id, cancellationToken);

        if (issued.IsFailure)
        {
            return TooManyLinks(issued.Error, reply, phone);
        }

        await contactLinker.LinkAsync(contact, account.Id, cancellationToken);

        if (account.PhoneNumber == phone.Value && !account.PhoneNumberConfirmed)
        {
            await userRepository.SetPhoneAsync(account.Id, phone, confirmed: true, cancellationToken);
        }

        return reply.SignIn(phone, account.DisplayName ?? contact.ProfileName, issued.Value.Url);
    }

    /// <summary>
    /// «Crear cuenta» con el registro abierto: una cuenta sin correo, con el número verificado y el nombre del perfil de
    /// WhatsApp (recortado al tope de la cuenta: el perfil admite 256), que después se cambia en Mi perfil. El idioma es español, como todo lo que dice el bot sin cuenta: el
    /// de la petición no existe, porque esto corre en segundo plano.
    /// </summary>
    public async Task<WhatsAppOutboundMessage> CreateAccountAsync(
        WhatsAppContact contact,
        PhoneNumber phone,
        CancellationToken cancellationToken)
    {
        var account = await userRepository.CreateAsync(
            email: null,
            phone,
            phoneConfirmed: true,
            AccountRules.FitExternalDisplayName(contact.ProfileName),
            UserCultures.Default,
            cancellationToken);
        var reply = Reply(account);
        var issued = await loginLinks.IssueAsync(account.Id, cancellationToken);

        if (issued.IsFailure)
        {
            return TooManyLinks(issued.Error, reply, phone);
        }

        await contactLinker.LinkAsync(contact, account.Id, cancellationToken);

        LogAccountCreated(logger);

        return reply.AccountCreated(phone, account.DisplayName, issued.Value.Url);
    }

    /// <summary>El emisor solo rechaza por sus límites (uno por minuto y 5 cada 15 minutos). Otro error es un bug.</summary>
    private static WhatsAppTextMessage TooManyLinks(Error error, BotReply reply, PhoneNumber phone) =>
        error.Code == LoginLinkErrors.TooManyRequestsCode
            ? reply.TooManyLinks(phone)
            : throw new InvalidOperationException($"Unexpected error issuing a login link: {error.Code}.");

    private BotReply Reply(UserAccount account) => BotReply.For(appName.Value, account);

    // Como todos los del bot, sin el número, el nombre ni el enlace.
    [LoggerMessage(Level = LogLevel.Information, Message = "The WhatsApp bot created an account for a new number with open registration")]
    private static partial void LogAccountCreated(ILogger logger);
}
