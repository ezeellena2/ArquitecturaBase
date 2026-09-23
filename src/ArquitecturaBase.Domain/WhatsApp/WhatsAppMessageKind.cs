namespace ArquitecturaBase.Domain.WhatsApp;

/// <summary>
/// Qué clase de mensaje es. Se guarda como texto, así que sumar un valor no cambia el significado de las filas.
/// </summary>
public enum WhatsAppMessageKind
{
    /// <summary>Un texto: el cuerpo es lo que escribió la persona o lo que mandó el bot.</summary>
    Text = 1,

    /// <summary>
    /// La persona tocó un botón de respuesta, de un mensaje interactivo o de una plantilla: el cuerpo es el título del
    /// botón y <see cref="WhatsAppMessage.ReplyId"/>, su identificador.
    /// </summary>
    ButtonReply = 2,

    /// <summary>Una foto, un audio, un video, un documento o un sticker. Del archivo no se guarda nada.</summary>
    Media = 3,

    /// <summary>Un aviso de WhatsApp, como el cambio de número de la persona.</summary>
    System = 4,

    /// <summary>Todo lo demás: una ubicación, una reacción o un tipo que Meta sume más adelante. Sin cuerpo.</summary>
    Other = 5,
}
