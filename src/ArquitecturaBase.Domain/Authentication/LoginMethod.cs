namespace ArquitecturaBase.Domain.Authentication;

/// <summary>
/// Con qué se intentó entrar. Se guarda como texto, así que sumar un valor no cambia el significado de las filas.
/// </summary>
public enum LoginMethod
{
    /// <summary>Código por correo.</summary>
    Code = 1,

    /// <summary>Cuenta de Google.</summary>
    Google = 2,

    /// <summary>Código por WhatsApp, pedido desde la web.</summary>
    WhatsAppCode = 3,

    /// <summary>
    /// Enlace de ingreso que mandaba el bot de WhatsApp. Se retiró el 2026-11-16 (B4); queda por las filas de antes.
    /// </summary>
    WhatsAppLink = 4,
}
