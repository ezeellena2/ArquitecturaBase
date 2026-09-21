namespace ArquitecturaBase.Domain.Settings;

/// <summary>Quién puede crear una cuenta (sección 4 del spec de la Fase 4).</summary>
public enum RegistrationMode
{
    /// <summary>Solo entra quien un administrador dio de alta.</summary>
    InviteOnly = 0,

    /// <summary>Cualquiera se crea la cuenta al ingresar por primera vez.</summary>
    Open = 1,
}
