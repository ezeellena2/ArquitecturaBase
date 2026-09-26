namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>
/// El cuerpo de POST /api/me/whatsapp/code: el número que la persona quiere vincular, tal como lo escribió, y el país
/// elegido.
/// </summary>
public sealed record RequestPhoneLinkCodeHttpRequest(string? Country, string? Number)
{
    // MVC registra los argumentos de la acción con ToString(): no revelar el número.
    public override string ToString() => nameof(RequestPhoneLinkCodeHttpRequest);
}
