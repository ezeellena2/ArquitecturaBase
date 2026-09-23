namespace ArquitecturaBase.Domain.WhatsApp;

/// <summary>
/// El estado de un mensaje saliente según los avisos de Meta. Un mensaje avanza de <see cref="Sent"/> a
/// <see cref="Delivered"/> y a <see cref="Read"/>; <see cref="Failed"/> es final. Se guarda como texto.
/// </summary>
public enum WhatsAppMessageStatus
{
    /// <summary>Salió de los servidores de Meta (un tilde).</summary>
    Sent = 1,

    /// <summary>Llegó al celular (dos tildes).</summary>
    Delivered = 2,

    /// <summary>La persona lo vio (dos tildes azules).</summary>
    Read = 3,

    /// <summary>No se pudo mandar o entregar. Guarda el código de error de Meta.</summary>
    Failed = 4,
}
