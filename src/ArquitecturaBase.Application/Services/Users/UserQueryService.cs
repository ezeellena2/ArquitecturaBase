using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Las lecturas de la administración de usuarios: el listado, sus conteos por opción de filtro y el detalle con la última
/// invitación, cuyo estado de entrega lo da la fuente de su canal (<see cref="IInvitationDeliveryStatusSource"/>), si
/// tiene. No escriben, así que no abren límite.
/// </summary>
internal sealed class UserQueryService(
    IUserReader userReader,
    IUserInvitationReader invitationReader,
    IEnumerable<IInvitationDeliveryStatusSource> deliveryStatuses,
    IPhoneNumberParser phoneNumbers,
    IRequestValidator validator,
    ILogger<UserQueryService> logger) : IUserQueryService
{
    public Task<Result<PagedResult<UserListItemResponse>>> ListUsersAsync(
        ListUsersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<PagedResult<UserListItemResponse>>>(logger, "ListUsers", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            var page = await userReader.ListUsersAsync(request, cancellationToken);
            return new PagedResult<UserListItemResponse>(
                [.. page.Items.Select(ToListItem)], page.Page, page.PageSize, page.TotalCount);
        });
    }

    public Task<Result<UserFilterCounts>> GetUserFilterCountsAsync(
        ListUsersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<UserFilterCounts>>(logger, "GetUserFilterCounts", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            return await userReader.CountByFilterOptionAsync(request, cancellationToken);
        });
    }

    public Task<Result<UserDetailResponse>> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<UserDetailResponse>>(logger, "GetUser", async () =>
        {
            if (await userReader.FindDetailAsync(userId, cancellationToken) is not { } detail)
            {
                return UserErrors.NotFound;
            }

            // Sin número, Create falla y queda en null, igual que el número.
            var phone = PhoneNumber.Create(detail.PhoneNumber);
            return new UserDetailResponse(
                detail.Id,
                detail.Email,
                detail.EmailConfirmed,
                detail.PhoneNumber,
                detail.PhoneNumberConfirmed,
                detail.DisplayName,
                detail.IsActive,
                detail.CreatedAtUtc,
                detail.Roles)
            {
                FormattedPhoneNumber = phone.IsSuccess ? phoneNumbers.FormatInternational(phone.Value) : null,
                LastInvitation = await invitationReader.FindLatestAsync(detail.Id, cancellationToken) is { } invitation
                    ? LastInvitation.From(invitation, await TrackedStatusAsync(invitation, cancellationToken))
                    : null,
            };
        });

    // Una que no se pudo mandar es fallida sin preguntar (LastInvitation.From). Si salió, el estado lo sabe la fuente de
    // su canal; sin fuente (el correo), no hay estado que seguir.
    private async Task<InvitationDeliveryStatus?> TrackedStatusAsync(UserInvitationRow invitation, CancellationToken cancellationToken)
    {
        if (invitation.SendFailed
            || deliveryStatuses.SingleOrDefault(source => source.Channel == invitation.Channel) is not { } source)
        {
            return null;
        }

        return await source.FindStatusAsync(invitation.ProviderMessageId, cancellationToken);
    }

    // Sin número, Create falla y queda en null, igual que el número.
    private UserListItemResponse ToListItem(UserListRow item)
    {
        var phone = PhoneNumber.Create(item.PhoneNumber);

        return new UserListItemResponse(
            item.Id,
            item.Email,
            item.PhoneNumber,
            item.PhoneNumberConfirmed,
            item.DisplayName,
            item.IsActive,
            item.CreatedAtUtc,
            item.Roles)
        {
            FormattedPhoneNumber = phone.IsSuccess ? phoneNumbers.FormatInternational(phone.Value) : null,
        };
    }
}
