namespace ArquitecturaBase.Domain.Authentication;

/// <summary>
/// Para qué se pidió un código de ingreso (sección 6.3 del spec del ingreso con WhatsApp). Un código solo sirve para
/// el propósito con el que se emitió.
/// </summary>
public enum LoginCodePurpose
{
    /// <summary>Entrar con el correo o el número, o crear la cuenta si el registro lo permite.</summary>
    SignIn = 1,

    /// <summary>
    /// Demostrar, desde el perfil, que un número o un correo es de quien lo quiere vincular. Solo sirve para la cuenta
    /// que lo pidió.
    /// </summary>
    VerifyDestination = 2,
}
