using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Infrastructure.Settings;

/// <summary>
/// Valor inicial del modo de registro, en Registration:Mode. Solo lo usa el seed al crear la fila: después manda
/// la base, porque el modo se cambia desde el panel (sección 5 del spec de la Fase 4).
/// </summary>
internal sealed class RegistrationOptions
{
    public const string SectionName = "Registration";

    public RegistrationMode Mode { get; init; } = RegistrationMode.InviteOnly;
}
