using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.CreateUser;

/// <summary>Alta de un correo por un administrador (sección 7 del spec de la Fase 4). Sin roles, la cuenta queda como User.</summary>
public sealed record CreateUserCommand(string? Email, string? DisplayName, IReadOnlyCollection<string>? Roles)
    : ICommand<Guid>;
