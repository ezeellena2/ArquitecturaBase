namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Correo y código recibido para verificar la dirección desde el perfil propio.</summary>
public sealed record ConfirmEmailRequest(string? Email, string? Code)
{
    // MVC registra los argumentos de la acción con ToString(): nunca revelar el código ni el correo.
    public override string ToString() => nameof(ConfirmEmailRequest);
}
