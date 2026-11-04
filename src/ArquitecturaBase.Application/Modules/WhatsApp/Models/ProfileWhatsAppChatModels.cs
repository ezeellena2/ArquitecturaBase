namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>Cómo está el chat de WhatsApp del número de la cuenta, para Mi perfil.</summary>
public enum ProfileWhatsAppChatState
{
    /// <summary>El chat del número confirmado está vinculado a la cuenta.</summary>
    Linked,

    /// <summary>El número ya escribió, pero su chat no está vinculado a la cuenta: se vincula confirmando el número.</summary>
    NotLinked,

    /// <summary>El número nunca le escribió al negocio, o la cuenta no tiene número: no hay chat que vincular.</summary>
    NoChat,
}

/// <summary>
/// El chat de WhatsApp de la cuenta: si está vinculado y hasta cuándo le pueden llegar los avisos, que es 24 horas después
/// del último mensaje de la persona (la ventana que exige mandarlos). Null si hoy no le llegan.
/// </summary>
public sealed record ProfileWhatsAppChatResponse(ProfileWhatsAppChatState State, DateTime? NoticesUntilUtc);

/// <summary>
/// Lo que el perfil lee de los contactos: el número y el último mensaje del chat vinculado a la cuenta, si tiene uno, y si
/// el número de la cuenta ya tiene chat.
/// </summary>
public sealed record WhatsAppProfileChatRow(string? LinkedWaId, DateTime? LinkedLastInboundAtUtc, bool NumberHasChat)
{
    public override string ToString() => "WhatsAppProfileChatRow { LinkedWaId = [REDACTED] }";
}
