using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.Arca.Configuration;

/// <summary>
/// La CUIT de la plataforma en la que las empresas delegan sus servicios (<c>Arca:Platform</c>, ADR 0027), con el nombre que
/// muestra la guía. Es de quien despliega la instalación, no de la empresa. Sin ella, el modo delegación no se ofrece.
/// </summary>
public sealed class ArcaPlatformOptions
{
    public string? Cuit { get; init; }

    public string? Name { get; init; }

    /// <summary>Las dos vacías, o una CUIT válida con un nombre de hasta 100 caracteres.</summary>
    public bool IsValid() => string.IsNullOrWhiteSpace(Cuit)
        ? string.IsNullOrWhiteSpace(Name)
        : CuitNumber.Normalize(Cuit) is not null && !string.IsNullOrWhiteSpace(Name) && Name.Trim().Length <= 100;

    /// <summary>La plataforma configurada, o null.</summary>
    public ArcaPlatformResponse? ToResponse() =>
        CuitNumber.Normalize(Cuit ?? string.Empty) is { } cuit && !string.IsNullOrWhiteSpace(Name) ? new(cuit, Name.Trim()) : null;
}
