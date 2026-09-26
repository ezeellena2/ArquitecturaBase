namespace ArquitecturaBase.Api.Contracts.Auth;

/// <summary>
/// El cuerpo de POST /account/login-code/whatsapp: el número como lo escribió la persona y el país elegido para
/// interpretar números sin prefijo.
/// </summary>
public sealed record RequestWhatsAppLoginCodeHttpRequest(string? Country, string? Number)
{
    // MVC registra los argumentos de la acción con ToString(): no incluir el número completo en esos logs.
    public override string ToString() => nameof(RequestWhatsAppLoginCodeHttpRequest);
}
