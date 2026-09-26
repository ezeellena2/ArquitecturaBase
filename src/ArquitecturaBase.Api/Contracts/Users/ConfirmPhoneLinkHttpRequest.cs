namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>El cuerpo de PUT /api/me/whatsapp: el número internacional y el código recibido por WhatsApp.</summary>
public sealed record ConfirmPhoneLinkHttpRequest(string? Phone, string? Code)
{
    // MVC registra los argumentos de la acción con ToString(): nunca revelar el número ni el código.
    public override string ToString() => nameof(ConfirmPhoneLinkHttpRequest);
}
