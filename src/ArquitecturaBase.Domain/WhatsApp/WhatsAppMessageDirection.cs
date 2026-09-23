namespace ArquitecturaBase.Domain.WhatsApp;

/// <summary>Para qué lado fue un mensaje. Se guarda como texto.</summary>
public enum WhatsAppMessageDirection
{
    /// <summary>Lo mandó la persona al bot.</summary>
    Inbound = 1,

    /// <summary>Lo mandó el bot a la persona.</summary>
    Outbound = 2,
}
