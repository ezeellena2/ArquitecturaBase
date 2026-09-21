using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.UpdateProfile;

/// <summary>El perfil propio (sección 9 del spec de la Fase 4). El usuario sale del token, no del cuerpo.</summary>
public sealed record UpdateProfileCommand(string? DisplayName, string? Culture, string? TimeZoneId) : ICommand;
