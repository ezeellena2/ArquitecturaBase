using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.GetUser;

internal sealed class GetUserQueryHandler(IIdentityService identityService)
    : IQueryHandler<GetUserQuery, UserDetail>
{
    public async Task<Result<UserDetail>> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await identityService.FindDetailAsync(query.UserId, cancellationToken) is { } detail
            ? detail
            : UserErrors.NotFound;
    }
}
