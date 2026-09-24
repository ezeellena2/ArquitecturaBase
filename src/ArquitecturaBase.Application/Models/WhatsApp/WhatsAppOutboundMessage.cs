using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Models.WhatsApp;

/// <summary>
/// Un mensaje para mandar por WhatsApp. Application dice <b>qué</b> se manda (sección 9 del spec) e Infrastructure
/// arma el JSON de Meta. La jerarquía es cerrada: un tipo por cada forma de mensaje que se usa.
/// Los límites de Meta se controlan al construirlo, con <see cref="ArgumentException"/>: pasarse es un error de
/// programación, no algo que decida quien escribe.
/// </summary>
public abstract record WhatsAppOutboundMessage
{
    /// <summary>El largo máximo del cuerpo de un mensaje interactivo.</summary>
    public const int MaxInteractiveBodyLength = 1024;

    /// <summary>El largo máximo del pie de un mensaje interactivo.</summary>
    public const int MaxFooterLength = 60;

    private protected WhatsAppOutboundMessage(PhoneNumber to)
    {
        ArgumentNullException.ThrowIfNull(to);

        To = to;
    }

    /// <summary>El destinatario, en formato internacional con el "+".</summary>
    public PhoneNumber To { get; }

    /// <summary>
    /// Lo que se puede guardar en el historial o mostrar sin riesgo: nunca el código ni la URL del enlace.
    /// </summary>
    public abstract string SafeSummary { get; }

    /// <summary>
    /// Un record imprime todas sus propiedades: si un mensaje terminara en un log, dejaría a la vista el código, el
    /// enlace y el número. Por eso solo imprime el tipo.
    /// </summary>
    public sealed override string ToString() => GetType().Name;
}

/// <summary>
/// Un texto suelto. El cuerpo se guarda tal cual en el historial, así que no lleva códigos ni enlaces: para eso están
/// <see cref="WhatsAppLoginCodeMessage"/> y <see cref="WhatsAppLinkButtonMessage"/>.
/// </summary>
public sealed record WhatsAppTextMessage : WhatsAppOutboundMessage
{
    public const int MaxBodyLength = 4096;

    public WhatsAppTextMessage(PhoneNumber to, string body)
        : base(to)
    {
        Body = WhatsAppMessageLimits.Require(body, MaxBodyLength, nameof(body));
    }

    public string Body { get; }

    public override string SafeSummary => Body;
}

/// <summary>
/// Un texto con un botón que abre una dirección (el <c>cta_url</c> de Meta). Es el mensaje del enlace de ingreso: la
/// URL lleva el token, así que no entra en <see cref="WhatsAppOutboundMessage.SafeSummary"/>.
/// </summary>
public sealed record WhatsAppLinkButtonMessage : WhatsAppOutboundMessage
{
    public const int MaxButtonTextLength = 20;

    public WhatsAppLinkButtonMessage(PhoneNumber to, string body, string buttonText, string url, string? footer = null)
        : base(to)
    {
        Body = WhatsAppMessageLimits.Require(body, MaxInteractiveBodyLength, nameof(body));
        ButtonText = WhatsAppMessageLimits.Require(buttonText, MaxButtonTextLength, nameof(buttonText));
        Footer = WhatsAppMessageLimits.Optional(footer, MaxFooterLength, nameof(footer));

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("The button URL must be an absolute http or https URL.", nameof(url));
        }

        Url = url;
    }

    public string Body { get; }

    public string ButtonText { get; }

    public string Url { get; }

    public string? Footer { get; }

    public override string SafeSummary => $"{Body} [{ButtonText}]";
}

/// <summary>Un botón de respuesta: WhatsApp devuelve su <see cref="Id"/> en el webhook cuando la persona lo toca.</summary>
public sealed record WhatsAppReplyButton
{
    public const int MaxIdLength = 256;
    public const int MaxTitleLength = 20;

    public WhatsAppReplyButton(string id, string title)
    {
        Id = WhatsAppMessageLimits.Require(id, MaxIdLength, nameof(id));
        Title = WhatsAppMessageLimits.Require(title, MaxTitleLength, nameof(title));
    }

    public string Id { get; }

    public string Title { get; }
}

/// <summary>Un texto con botones de respuesta, de 1 a 3, con ids y títulos distintos (así lo pide Meta).</summary>
public sealed record WhatsAppReplyButtonsMessage : WhatsAppOutboundMessage
{
    public const int MaxButtons = 3;

    public WhatsAppReplyButtonsMessage(
        PhoneNumber to,
        string body,
        IReadOnlyList<WhatsAppReplyButton> buttons,
        string? footer = null)
        : base(to)
    {
        ArgumentNullException.ThrowIfNull(buttons);

        Body = WhatsAppMessageLimits.Require(body, MaxInteractiveBodyLength, nameof(body));
        Footer = WhatsAppMessageLimits.Optional(footer, MaxFooterLength, nameof(footer));

        if (buttons.Count is 0 or > MaxButtons)
        {
            throw new ArgumentException($"A reply buttons message needs from 1 to {MaxButtons} buttons.", nameof(buttons));
        }

        if (buttons.DistinctBy(button => button.Id, StringComparer.Ordinal).Count() != buttons.Count
            || buttons.DistinctBy(button => button.Title, StringComparer.Ordinal).Count() != buttons.Count)
        {
            throw new ArgumentException("Reply button ids and titles must be unique.", nameof(buttons));
        }

        Buttons = [.. buttons];
    }

    public string Body { get; }

    public IReadOnlyList<WhatsAppReplyButton> Buttons { get; }

    public string? Footer { get; }

    public override string SafeSummary => $"{Body} [{string.Join(" | ", Buttons.Select(button => button.Title))}]";
}

/// <summary>
/// El código de ingreso. Sale con la plantilla de autenticación aprobada en Meta, que pone el texto: acá van el idioma
/// (el mismo código que la cultura del perfil, "es" o "en") y el código. Qué plantilla se usa lo decide Infrastructure
/// con su configuración (<c>WhatsApp:Templates:LoginCode</c>), no quien pide el mensaje.
/// </summary>
public sealed record WhatsAppLoginCodeMessage : WhatsAppOutboundMessage
{
    /// <summary>Meta no acepta códigos más largos.</summary>
    public const int MaxCodeLength = 15;

    private const int MaxLanguageCodeLength = 16;

    public WhatsAppLoginCodeMessage(PhoneNumber to, string languageCode, string code)
        : base(to)
    {
        LanguageCode = WhatsAppMessageLimits.Require(languageCode, MaxLanguageCodeLength, nameof(languageCode));
        Code = WhatsAppMessageLimits.Require(code, MaxCodeLength, nameof(code));
    }

    public string LanguageCode { get; }

    public string Code { get; }

    public override string SafeSummary => "[código]";
}

/// <summary>
/// La invitación de un administrador (sección 6.6 del spec del ingreso con WhatsApp). Sale con la plantilla aprobada en
/// Meta, que pone el texto: acá van el idioma de la cuenta, el nombre de la persona (<c>{{1}}</c>), el del sistema
/// (<c>{{2}}</c>) y el payload del botón «Quiero entrar», que vuelve en el webhook cuando la persona lo toca. No lleva
/// nada que sirva para entrar: el enlace lo manda el bot cuando la persona toca el botón. Qué plantilla se usa lo decide
/// Infrastructure con su configuración (<c>WhatsApp:Templates:Invitation</c>). Lleva la cuenta y la invitación para que
/// la cola, después de mandarla, le deje a la invitación el id que devolvió Meta, o la marque como fallida.
/// </summary>
public sealed record WhatsAppInvitationMessage : WhatsAppOutboundMessage
{
    private const int MaxLanguageCodeLength = 16;

    /// <summary>Un parámetro de plantilla es parte del cuerpo, que en Meta no pasa de 1024 caracteres.</summary>
    private const int MaxParameterLength = MaxInteractiveBodyLength;

    public WhatsAppInvitationMessage(
        PhoneNumber to,
        Guid userId,
        Guid invitationId,
        string languageCode,
        string name,
        string appName,
        string replyPayload)
        : base(to)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("The invitation message needs the invited account.", nameof(userId));
        }

        if (invitationId == Guid.Empty)
        {
            throw new ArgumentException("The invitation message needs its invitation.", nameof(invitationId));
        }

        UserId = userId;
        InvitationId = invitationId;
        LanguageCode = WhatsAppMessageLimits.Require(languageCode, MaxLanguageCodeLength, nameof(languageCode));
        Name = WhatsAppMessageLimits.Require(SingleLine(name), MaxParameterLength, nameof(name));
        AppName = WhatsAppMessageLimits.Require(SingleLine(appName), MaxParameterLength, nameof(appName));
        ReplyPayload = WhatsAppMessageLimits.Require(replyPayload, WhatsAppReplyButton.MaxIdLength, nameof(replyPayload));
    }

    /// <summary>La cuenta invitada.</summary>
    public Guid UserId { get; }

    /// <summary>La invitación guardada que corresponde a este mensaje.</summary>
    public Guid InvitationId { get; }

    public string LanguageCode { get; }

    /// <summary>El nombre de la persona, en una sola línea: la plantilla la saluda con él.</summary>
    public string Name { get; }

    /// <summary>El nombre del sistema (<c>Email:AppName</c>).</summary>
    public string AppName { get; }

    /// <summary>El payload del botón de respuesta rápida: <c>WANT_TO_ENTER</c>, el que entiende el bot.</summary>
    public string ReplyPayload { get; }

    /// <summary>Sin el nombre: el historial no necesita repetir datos de la persona que ya están en su cuenta.</summary>
    public override string SafeSummary => "[invitación]";

    // Meta rechaza un parámetro de plantilla con saltos de línea, tabulaciones o más de cuatro espacios seguidos.
    private static string SingleLine(string? value) =>
        value is null ? string.Empty : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Los controles de largo de Meta, compartidos por los mensajes y los botones.</summary>
internal static class WhatsAppMessageLimits
{
    public static string Require(string? value, int maxLength, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);

        if (value.Length > maxLength)
        {
            throw new ArgumentException($"The value cannot be longer than {maxLength} characters.", paramName);
        }

        return value;
    }

    public static string? Optional(string? value, int maxLength, string paramName) =>
        string.IsNullOrWhiteSpace(value) ? null : Require(value, maxLength, paramName);
}
