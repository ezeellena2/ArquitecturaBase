using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.SetUserActive;

/// <summary>Activa o desactiva una cuenta. Desactivar corta también el acceso ya emitido.</summary>
public sealed record SetUserActiveCommand(Guid UserId, bool IsActive) : ICommand;
