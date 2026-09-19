using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Infrastructure.Emails;

internal sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Nombre del producto en los emails. Provisorio hasta definir el definitivo (sección 11 del spec).</summary>
    [Required]
    public string AppName { get; init; } = "Arquitectura Base";

    /// <summary>PNG con URL absoluta: Gmail no muestra SVG (sección 8). Sin logo, el encabezado muestra el nombre.</summary>
    [Url]
    public string? LogoUrl { get; init; }
}
