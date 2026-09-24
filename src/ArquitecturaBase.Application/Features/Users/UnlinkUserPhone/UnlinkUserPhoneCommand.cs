using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.UnlinkUserPhone;

/// <summary>Un administrador desvincula el WhatsApp de una cuenta: el caso del teléfono perdido o robado.</summary>
public sealed record UnlinkUserPhoneCommand(Guid UserId) : ICommand;
