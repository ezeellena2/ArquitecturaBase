using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Infrastructure.Emails;

internal sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailDelivery Delivery { get; init; } = EmailDelivery.Smtp;

    /// <summary>Nombre del producto en los emails. Provisorio hasta definir el definitivo (sección 11 del spec).</summary>
    [Required]
    public string AppName { get; init; } = "Acceso";

    /// <summary>PNG con URL absoluta: Gmail no muestra SVG (sección 8). Sin logo, el encabezado muestra el nombre.</summary>
    [Url]
    public string? LogoUrl { get; init; }

    /// <summary>Carpeta de los .eml con <see cref="EmailDelivery.PickupDirectory"/>, relativa a la raíz de la Api.</summary>
    [Required]
    public string PickupDirectory { get; init; } = ".emails";

    /// <summary>Espera antes del primer reintento; se duplica en cada intento.</summary>
    [Range(0, 300)]
    public int RetryDelaySeconds { get; init; } = 2;

    /// <summary>
    /// Correos que puede tener la cola antes de rechazar los nuevos, como <c>WhatsApp:QueueCapacity</c>. Un correo
    /// rechazado no se reintenta: el código queda sin enviar y la invitación, como no enviada (backend.md, "Colas en
    /// memoria").
    /// </summary>
    [Range(1, 10_000)]
    public int QueueCapacity { get; init; } = 100;
}
