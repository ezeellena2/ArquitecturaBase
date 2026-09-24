using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Users.GetUsers;

internal sealed class GetUsersQueryHandler(IIdentityService identityService, IPhoneNumberParser phoneNumbers)
    : IQueryHandler<GetUsersQuery, PagedResult<UserListItem>>
{
    public async Task<Result<PagedResult<UserListItem>>> Handle(GetUsersQuery query, CancellationToken cancellationToken)
    {
        var page = await identityService.ListUsersAsync(query, cancellationToken);

        return page with { Items = [.. page.Items.Select(WithFormattedPhone)] };
    }

    // Sin número, Create falla y queda en null, igual que el número.
    private UserListItem WithFormattedPhone(UserListItem item) =>
        PhoneNumber.Create(item.PhoneNumber) is { IsSuccess: true } phone
            ? item with { FormattedPhoneNumber = phoneNumbers.FormatInternational(phone.Value) }
            : item;
}
