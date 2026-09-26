namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Correo y código recibido para verificar la dirección desde el perfil propio.</summary>
public sealed record ConfirmEmailRequest(string? Email, string? Code);
