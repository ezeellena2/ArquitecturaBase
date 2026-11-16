namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// Correo y código recibido para verificar la dirección desde el perfil propio. Si la cuenta tiene un número verificado,
/// también el código que se le mandó a ese número (<paramref name="PhoneCode"/>, N11).
/// </summary>
public sealed record ConfirmEmailRequest(string? Email, string? Code, string? PhoneCode = null);
