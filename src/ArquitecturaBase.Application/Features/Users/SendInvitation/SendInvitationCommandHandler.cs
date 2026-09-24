using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.SendInvitation;

/// <summary>
/// Reenvía la invitación con las mismas reglas que el alta (<see cref="UserInvitationSender.Check"/>), contra lo que tiene
/// la cuenta: su correo, su número y su nombre. Una cuenta borrada no se encuentra y una desactivada no se invita, porque
/// no podría entrar. Entre una invitación y la siguiente a la misma cuenta pasa al menos un minuto
/// (<see cref="UserInvitation.ResendCooldown"/>); una que no se pudo mandar no hace esperar, porque a la persona no le
/// llegó nada.
/// </summary>
internal sealed class SendInvitationCommandHandler(
    IIdentityService identityService,
    IUserInvitationRepository invitations,
    UserInvitationSender sender,
    TimeProvider timeProvider)
    : ICommandHandler<SendInvitationCommand>
{
    public async Task<Result> Handle(SendInvitationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Primero el lock: dos reenvíos al mismo tiempo pasan de a uno, y el segundo ve la invitación del primero.
        await invitations.LockAccountAsync(command.UserId, cancellationToken);

        var user = await identityService.FindByIdAsync(command.UserId, cancellationToken);

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (!user.IsActive)
        {
            return UserInvitationErrors.UserInactive;
        }

        var channel = command.Channel!.Value;
        var allowed = sender.Check(
            channel,
            command.Consent,
            user.DisplayName,
            user.Email is not null,
            user.PhoneNumber is not null,
            InvitationFields.OfResend);

        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var wait = (await invitations.GetLatestSentAsync(user.Id, cancellationToken))
            ?.WaitBeforeAnother(timeProvider.GetUtcNow().UtcDateTime) ?? TimeSpan.Zero;

        if (wait > TimeSpan.Zero)
        {
            return UserInvitationErrors.TooManyRequests((int)Math.Ceiling(wait.TotalSeconds));
        }

        await sender.SendAsync(user, channel, cancellationToken);

        return Result.Success();
    }
}
