using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.SendInvitation;

/// <summary>
/// Reenviar la invitación a una cuenta, por correo o por WhatsApp (sección 12 del spec del ingreso con WhatsApp). Para
/// WhatsApp, <see cref="Consent"/> es la confirmación de que la persona aceptó recibir mensajes.
/// </summary>
public sealed record SendInvitationCommand(Guid UserId, UserInvitationChannel? Channel, bool Consent) : ICommand;
