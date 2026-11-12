namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>
/// Qué clase de mensaje es. Se guarda como texto, así que sumar un valor no cambia el significado de las filas.
/// </summary>
public enum WhatsAppMessageKind
{
    /// <summary>Un texto: el cuerpo es lo que escribió la persona o lo que mandó el sistema o el operador.</summary>
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

    /// <summary>
    /// Todo lo demás: una reacción o un tipo que Meta sume más adelante. Sin cuerpo. Una ubicación anterior a W1-A y un contacto
    /// anterior a W3 quedaron acá: no se reclasifican.
    /// </summary>
    Other = 5,

    /// <summary>
    /// Solo en un saliente: un texto con botones, sean de respuesta o con un enlace. El cuerpo es su resumen seguro,
    /// con los títulos de los botones y nunca la URL.
    /// </summary>
    Interactive = 6,

    /// <summary>Solo en un saliente: una plantilla aprobada en Meta, como la del código. El cuerpo es su resumen seguro.</summary>
    Template = 7,
    Flow = 8,
    FlowReply = 9,
    Document = 10,
    Audio = 11,

    /// <summary>
    /// Solo en un entrante: un documento que es una planilla (Excel o CSV), por su nombre o por el tipo que manda Meta. El
    /// cuerpo es su <c>caption</c>; el descriptor, la copia y lo preparado están en las columnas de la planilla.
    /// <see cref="Document"/> es el documento saliente.
    /// </summary>
    Spreadsheet = 12,

    /// <summary>
    /// Solo en un entrante: una ubicación (W1-A). Sin cuerpo: el punto va en <see cref="WhatsAppMessage.Latitude"/> y
    /// <see cref="WhatsAppMessage.Longitude"/>, y la dirección de un lugar, si Meta la manda, en
    /// <see cref="WhatsAppMessage.LocationAddress"/> (W3); el nombre del lugar no se guarda.
    /// </summary>
    Location = 13,

    /// <summary>Solo en un saliente: el pedido de ubicación que va antes del Flow de su envío (W1-A). El cuerpo es su texto.</summary>
    LocationRequest = 14,

    /// <summary>
    /// Solo en un entrante: una foto (JPEG o PNG) o un documento que no es una planilla (PDF, Word o cualquier otro). El cuerpo es su
    /// <c>caption</c>; el descriptor, la copia y la lectura están en las columnas del documento. <see cref="Document"/> es el saliente.
    /// </summary>
    ReceivedDocument = 15,

    /// <summary>
    /// Solo en un entrante: un contacto que comparte la persona (W3). Sin cuerpo: el nombre y los teléfonos de su primera tarjeta van en
    /// <see cref="WhatsAppMessage.ContactName"/> y <see cref="WhatsAppMessage.ContactPhonesJson"/>, tal como vinieron.
    /// </summary>
    Contact = 16,
}
