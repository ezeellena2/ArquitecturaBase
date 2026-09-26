namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Correo que la persona quiere agregar a su propia cuenta.</summary>
public sealed record RequestEmailCodeRequest(string? Email);
