namespace ArquitecturaBase.Domain.Authentication;

/// <summary>
/// Por dónde sale un código de ingreso (sección 6.3 del spec del ingreso con WhatsApp). Se guarda como texto, con el
/// nombre: cambiar un nombre pide una migración de datos (LoginCodePhoneChannel pasó 'WhatsApp' a 'Phone').
/// </summary>
public enum LoginCodeChannel
{
    /// <summary>Por correo, a una dirección normalizada.</summary>
    Email = 1,

    /// <summary>Por teléfono, a un número en formato internacional (con WhatsApp, por su chat).</summary>
    Phone = 2,
}
