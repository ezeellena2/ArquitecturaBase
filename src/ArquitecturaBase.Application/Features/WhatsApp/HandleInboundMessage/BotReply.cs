using System.Globalization;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.WhatsApp.HandleInboundMessage;

/// <summary>
/// Las respuestas del bot en un idioma, con los textos del tablero "WhatsApp · Conversaciones con el bot" (Bot.resx),
/// el nombre del sistema y el de la persona. Solo arma los mensajes: cuál corresponde lo decide el handler. Los botones
/// que abren el navegador ("Entrar" e "Ir a la web") son botones con enlace; los que contestan en el chat, botones de
/// respuesta con los ids de <see cref="BotButtons"/>.
/// </summary>
internal sealed class BotReply
{
    /// <summary>Lo que dura un enlace, para decírselo a la persona: los 10 minutos fijos del dominio.</summary>
    private static readonly int LinkMinutes = (int)LoginLink.Lifetime.TotalMinutes;

    private readonly string _appName;
    private readonly CultureInfo _culture;

    private BotReply(string appName, CultureInfo culture)
    {
        _appName = appName;
        _culture = culture;
    }

    /// <summary>
    /// En el idioma de la cuenta; sin cuenta, o con un idioma que el bot no habla, en español (sección 8 del spec del
    /// ingreso con WhatsApp). Nunca el de la petición: el bot contesta desde segundo plano.
    /// </summary>
    public static BotReply For(string appName, UserAccount? account)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        var culture = account is not null && UserCultures.IsSupported(account.Culture) ? account.Culture : UserCultures.Default;

        return new BotReply(appName, CultureInfo.GetCultureInfo(culture));
    }

    /// <summary>Fila 1 del tablero: el enlace para entrar, con el pie que aclara para qué sirve el chat por ahora.</summary>
    public WhatsAppLinkButtonMessage SignIn(PhoneNumber to, string? name, string url) =>
        new(
            to,
            name is null
                ? Text("SignIn.BodyWithoutName", _appName, LinkMinutes)
                : Text("SignIn.Body", name, _appName, LinkMinutes),
            Text("SignIn.Button"),
            url,
            Text("SignIn.Footer"));

    /// <summary>Fila 2: la cuenta recién creada desde el chat, con el enlace para abrirla.</summary>
    public WhatsAppLinkButtonMessage AccountCreated(PhoneNumber to, string? name, string url) =>
        new(
            to,
            name is null
                ? Text("AccountCreated.BodyWithoutName", LinkMinutes)
                : Text("AccountCreated.Body", name, LinkMinutes),
            Text("SignIn.Button"),
            url);

    /// <summary>Fila 2: un número sin cuenta con el registro abierto. La pregunta, con sus dos botones.</summary>
    public WhatsAppReplyButtonsMessage NoAccount(PhoneNumber to) =>
        new(
            to,
            Text("NoAccount.Body"),
            [
                new WhatsAppReplyButton(BotButtons.CreateAccount, Text("NoAccount.CreateAccountButton")),
                new WhatsAppReplyButton(BotButtons.HaveAccount, Text("NoAccount.HaveAccountButton")),
            ]);

    /// <summary>Fila 3: «Ya tengo cuenta». El número se vincula desde la web, entrando con el correo.</summary>
    public WhatsAppLinkButtonMessage HaveAccount(PhoneNumber to, string webUrl) =>
        new(to, Text("HaveAccount.Body"), Text("GoToWebButton"), webUrl);

    /// <summary>Fila 4: sin cuenta y con el registro solo por invitación. Habla solo del número que escribe.</summary>
    public WhatsAppLinkButtonMessage NotInvited(PhoneNumber to, string webUrl) =>
        new(to, Text("NotInvited.Body", _appName), Text("GoToWebButton"), webUrl);

    /// <summary>Fila 5: la cuenta está deshabilitada, bloqueada o borrada. Nada más.</summary>
    public WhatsAppTextMessage Disabled(PhoneNumber to) => new(to, Text("Disabled.Body"));

    /// <summary>Fila 5: pidió otro enlace antes de que se pueda emitir uno nuevo.</summary>
    public WhatsAppTextMessage TooManyLinks(PhoneNumber to) => new(to, Text("TooManyLinks.Body", LinkMinutes));

    private string Text(string key, params object[] args) => BotTexts.Get(key, _culture, args);
}
