using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.ConfirmEmail;

/// <summary>
/// El código que llegó por correo para agregar <see cref="Email"/> a la propia cuenta. Los cambios se guardan aunque
/// falle, porque los intentos fallidos se descuentan del código.
/// </summary>
public sealed record ConfirmEmailCommand(string? Email, string? Code) : ICommand, IPersistChangesOnFailure;
