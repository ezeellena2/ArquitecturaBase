namespace ArquitecturaBase.Domain.Authentication;

/// <summary>Por dónde sale un código de ingreso (sección 6.3 del spec del ingreso con WhatsApp).</summary>
public enum LoginCodeChannel
{
    /// <summary>Por correo, a una dirección normalizada.</summary>
    Email = 1,

    /// <summary>Por WhatsApp, a un número en formato internacional.</summary>
    WhatsApp = 2,
}
