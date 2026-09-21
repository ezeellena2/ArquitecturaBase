using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.UpdateProfile;

internal sealed class UpdateProfileCommandHandler(IIdentityService identityService, ICurrentUser currentUser)
    : ICommandHandler<UpdateProfileCommand>
{
    public async Task<Result> Handle(UpdateProfileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (currentUser.UserId is not { } userId
            || await identityService.FindByIdAsync(userId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        await identityService.UpdateProfileAsync(
            userId, command.DisplayName, command.Culture!, command.TimeZoneId!, cancellationToken);

        return Result.Success();
    }
}
