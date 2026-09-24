using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.CreateUser;

/// <summary>
/// Alta de una cuenta por un administrador (sección 7 del spec de la Fase 4 y sección 12 del spec del ingreso con
/// WhatsApp): un correo, un número o los dos, y, si se pide, una invitación. Sin roles, la cuenta queda como User.
/// </summary>
public sealed record CreateUserCommand(
    string? Email,
    string? DisplayName,
    IReadOnlyCollection<string>? Roles,
    PhoneNumberInput? Phone = null,
    InvitationRequest? Invitation = null)
    : ICommand<Guid>;
