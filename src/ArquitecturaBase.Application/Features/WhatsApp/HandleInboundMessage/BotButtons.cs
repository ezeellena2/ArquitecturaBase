namespace ArquitecturaBase.Application.Features.WhatsApp.HandleInboundMessage;

/// <summary>
/// Los identificadores de los botones que entiende el bot (sección 8 del spec del ingreso con WhatsApp). Vuelven en el
/// webhook cuando la persona toca uno (el id de un botón de respuesta o el payload del botón de una plantilla), y son
/// todo lo que el bot sabe de la conversación: no guarda estado, así que no hay charlas a medio camino que vencer.
/// </summary>
public static class BotButtons
{
    /// <summary>«Crear cuenta», de la pregunta a un número sin cuenta con el registro abierto.</summary>
    public const string CreateAccount = "CREATE_ACCOUNT";

    /// <summary>«Ya tengo cuenta», de la misma pregunta.</summary>
    public const string HaveAccount = "HAVE_ACCOUNT";

    /// <summary>«Quiero entrar», el botón de la plantilla de invitación: se responde igual que a una cuenta activa.</summary>
    public const string WantToEnter = "WANT_TO_ENTER";

    /// <summary>
    /// «No pedí un código», de la plantilla de autenticación. No se responde: quien lo toca no pidió nada, y el código
    /// vence solo a los 10 minutos.
    /// </summary>
    public const string DidNotRequestCode = "DID_NOT_REQUEST_CODE";

    /// <summary>Si el bot decide algo distinto con ese botón. Cualquier otro se contesta como un texto.</summary>
    public static bool IsKnown(string? replyId) => replyId is CreateAccount or HaveAccount or WantToEnter;
}
