using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Users.CreateUser;

internal sealed class CreateUserCommandHandler(IIdentityService identityService)
    : ICommandHandler<CreateUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var emailResult = Email.Create(command.Email);

        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;

        // Sin roles en el alta, la cuenta queda igual que una que se creó sola al ingresar.
        IReadOnlyCollection<string> roles = command.Roles is { Count: > 0 }
            ? [.. command.Roles.Distinct(StringComparer.Ordinal)]
            : [SystemRoles.User];

        var known = await identityService.ListRoleNamesAsync(cancellationToken);

        if (roles.Any(role => !known.Contains(role, StringComparer.Ordinal)))
        {
            return RoleErrors.NotFound;
        }

        if (await identityService.FindByEmailAsync(email, cancellationToken) is not null)
        {
            return UserErrors.AlreadyExists;
        }

        // El correo es único: una cuenta borrada se restaura en lugar de fallar, y queda con los roles del alta,
        // no con los que tenía antes (sección 7 del spec de la Fase 4).
        var deleted = await identityService.FindDeletedByEmailAsync(email, cancellationToken);

        Guid userId;

        if (deleted is null)
        {
            var created = await identityService.CreateAsync(
                email, command.DisplayName, UserCultures.FromCurrentRequest(), cancellationToken);
            userId = created.Id;
        }
        else
        {
            userId = deleted.Id;
            await identityService.RestoreAsync(userId, command.DisplayName, cancellationToken);
        }

        await identityService.SetRolesAsync(userId, roles, cancellationToken);

        return userId;
    }
}
