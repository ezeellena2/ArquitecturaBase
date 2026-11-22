using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Infrastructure.Emails;

internal sealed class EmailOptions
{
    public const string SectionName = "Email";

    // RC6-B: the application name and the email logo are not here any more; they live in Configuración › Apariencia.
    public EmailDelivery Delivery { get; init; } = EmailDelivery.Smtp;

    /// <summary>Carpeta de los .eml con <see cref="EmailDelivery.PickupDirectory"/>, relativa a la raíz de la Api.</summary>
    [Required]
    public string PickupDirectory { get; init; } = ".emails";

    /// <summary>Espera antes del primer reintento; se duplica en cada intento. Sin 0: un servidor caído no se reintenta sin pausa.</summary>
    [Range(1, 300)]
    public int RetryDelaySeconds { get; init; } = 2;

    /// <summary>
    /// Correos que puede tener la cola antes de rechazar los nuevos, como <c>WhatsApp:QueueCapacity</c>. Un correo
    /// rechazado no se reintenta: el código queda sin enviar y la invitación, como no enviada (backend.md, "Colas en
    /// memoria").
    /// </summary>
    [Range(1, 10_000)]
    public int QueueCapacity { get; init; } = 100;
}
