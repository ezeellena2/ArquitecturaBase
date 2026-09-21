using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.DeleteUser;

/// <summary>Borrado lógico de una cuenta. Como desactivar, corta el acceso ya emitido.</summary>
public sealed record DeleteUserCommand(Guid UserId) : ICommand;
