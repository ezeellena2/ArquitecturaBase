namespace ArquitecturaBase.Application.Interfaces.Integrations;

/// <summary>
/// Le avisa al procesador de los mensajes entrantes que llegó algo para el bot (sección 7 del spec del ingreso con
/// WhatsApp). El webhook avisa después de guardar, así el procesador encuentra las filas. Es un aviso en memoria: si se
/// pierde (la app se reinicia, o el aviso llega a otra instancia), la revisión periódica de la tabla encuentra igual
/// los mensajes pendientes.
/// </summary>
public interface IWhatsAppInboundSignal
{
    /// <summary>Nunca espera. Varios avisos seguidos despiertan al procesador una sola vez.</summary>
    void Notify();
}
