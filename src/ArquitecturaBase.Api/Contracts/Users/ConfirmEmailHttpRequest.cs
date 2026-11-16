namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>
/// El cuerpo de PUT /api/me/email: el correo y el código recibido para verificarlo y, si la cuenta tiene un número
/// verificado, el código que llegó a ese número (N11).
/// </summary>
public sealed record ConfirmEmailHttpRequest(string? Email, string? Code, string? PhoneCode = null)
{
    // MVC registra los argumentos de la acción con ToString(): nunca revelar los códigos ni el correo.
    public override string ToString() => nameof(ConfirmEmailHttpRequest);
}
