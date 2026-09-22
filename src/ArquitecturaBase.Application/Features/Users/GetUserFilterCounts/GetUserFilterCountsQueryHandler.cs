using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Features.Users.GetUserFilterCounts;

internal sealed class GetUserFilterCountsQueryHandler(IIdentityService identityService)
    : IQueryHandler<GetUserFilterCountsQuery, UserFilterCounts>
{
    public async Task<Result<UserFilterCounts>> Handle(GetUserFilterCountsQuery query, CancellationToken cancellationToken) =>
        await identityService.GetUserFilterCountsAsync(query, cancellationToken);
}
