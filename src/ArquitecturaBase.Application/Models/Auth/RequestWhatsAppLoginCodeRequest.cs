namespace ArquitecturaBase.Application.Models.Auth;

/// <summary>El número como lo escribió la persona y el país elegido para interpretar números sin prefijo.</summary>
public sealed record RequestWhatsAppLoginCodeRequest(string? Country, string? Number)
{
    // MVC registra los argumentos de la acción: no incluir el número completo en esos logs.
    public override string ToString() => nameof(RequestWhatsAppLoginCodeRequest);
}
