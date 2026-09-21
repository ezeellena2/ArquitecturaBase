using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Settings;

/// <summary>
/// Ajustes del sistema. Es una tabla de una sola fila y la garantía es la clave primaria: siempre vale
/// <see cref="SingletonId"/>, así que un segundo INSERT choca con la PK. Es auditable porque un ajuste que decide
/// quién entra tiene que dejar rastro de quién lo cambió y cuándo (sección 5 del spec de la Fase 4).
/// Cada ajuste nuevo es una propiedad con su tipo y su migración, no un par clave-valor sin forma.
/// </summary>
public sealed class SystemSettings : Entity, IAuditable
{
    /// <summary>La clave de la única fila, como texto, para poder escribirla en el CHECK de la base.</summary>
    public const string SingletonIdValue = "00000000-0000-0000-0000-000000000001";

    /// <summary>La clave de la única fila.</summary>
    public static readonly Guid SingletonId = new(SingletonIdValue);

    // Para EF Core y para Create.
    private SystemSettings()
        : base(SingletonId)
    {
    }

    public RegistrationMode RegistrationMode { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public static SystemSettings Create(RegistrationMode registrationMode) =>
        new() { RegistrationMode = registrationMode };

    public void SetRegistrationMode(RegistrationMode registrationMode) => RegistrationMode = registrationMode;
}
