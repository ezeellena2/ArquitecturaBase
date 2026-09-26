namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>El cuerpo de PUT /api/me/email: el correo y el código recibido para verificarlo.</summary>
public sealed record ConfirmEmailHttpRequest(string? Email, string? Code)
{
    // MVC registra los argumentos de la acción con ToString(): nunca revelar el código ni el correo.
    public override string ToString() => nameof(ConfirmEmailHttpRequest);
}
