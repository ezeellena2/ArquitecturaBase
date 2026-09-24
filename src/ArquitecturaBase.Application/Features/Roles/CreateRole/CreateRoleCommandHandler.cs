using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Roles.CreateRole;

internal sealed class CreateRoleCommandHandler(IIdentityService identityService)
    : ICommandHandler<CreateRoleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = command.Name!.Trim();

        if (await identityService.RoleNameExistsAsync(name, excludedRoleId: null, cancellationToken))
        {
            return RoleErrors.AlreadyExists;
        }

        IReadOnlyCollection<string> permissions = [.. (command.Permissions ?? []).Distinct(StringComparer.Ordinal)];

        // Un rol nuevo no lo tiene nadie todavía, así que no hay caché que invalidar.
        return await identityService.CreateRoleAsync(name, command.Description, permissions, cancellationToken);
    }
}
