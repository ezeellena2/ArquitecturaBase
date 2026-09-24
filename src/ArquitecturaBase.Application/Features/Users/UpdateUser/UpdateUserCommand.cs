using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.UpdateUser;

/// <summary>
/// Nombre, roles y, opcionalmente, un correo o un número nuevos (sección 12 del spec del ingreso con WhatsApp). Los roles
/// reemplazan a los que tenía, no se suman. El correo y el número ausentes (o vacíos) no cambian: por acá no se borra un
/// medio de ingreso, para eso está desvincular.
/// </summary>
public sealed record UpdateUserCommand(
    Guid UserId,
    string? DisplayName,
    IReadOnlyCollection<string>? Roles,
    string? Email = null,
    PhoneNumberInput? Phone = null)
    : ICommand;
