using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Connection;

internal sealed class ArcaAccessTicketStore(IArcaConnectionRepository repository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
    : IArcaAccessTicketStore
{
    public async Task<ArcaTicketLeaseResponse> LeaseAsync(Guid certificateId, ArcaServiceCode service, Guid owner, TimeSpan margin,
        TimeSpan leaseDuration, DateTime? staleExpiresAtUtc, CancellationToken cancellationToken)
    {
        var result = await unitOfWork.ExecuteInTransactionAsync(
            token => LeaseCoreAsync(certificateId, service, owner, margin, leaseDuration, staleExpiresAtUtc, token), CommitPolicy.OnSuccess,
            cancellationToken);
        return result.Value;
    }

    public async Task<bool> StoreAsync(Guid certificateId, ArcaServiceCode service, Guid owner, string protectedCredentials,
        DateTime expiresAtUtc, CancellationToken cancellationToken)
    {
        var result = await unitOfWork.ExecuteInTransactionAsync(
            token => ChangeCoreAsync(certificateId, service, ticket => ticket.Store(owner, protectedCredentials, expiresAtUtc), token),
            CommitPolicy.OnSuccess, cancellationToken);
        return result.Value;
    }

    public async Task<bool> BlockAsync(Guid certificateId, ArcaServiceCode service, Guid owner, DateTime untilUtc, CancellationToken cancellationToken)
    {
        var result = await unitOfWork.ExecuteInTransactionAsync(
            token => ChangeCoreAsync(certificateId, service, ticket => ticket.Block(owner, untilUtc), token), CommitPolicy.OnSuccess, cancellationToken);
        return result.Value;
    }

    public async Task<bool> ReleaseAsync(Guid certificateId, ArcaServiceCode service, Guid owner, CancellationToken cancellationToken)
    {
        var result = await unitOfWork.ExecuteInTransactionAsync(
            token => ChangeCoreAsync(certificateId, service, ticket => ticket.Release(owner), token), CommitPolicy.OnSuccess, cancellationToken);
        return result.Value;
    }

    private async Task<Result<ArcaTicketLeaseResponse>> LeaseCoreAsync(Guid certificateId, ArcaServiceCode service, Guid owner,
        TimeSpan margin, TimeSpan leaseDuration, DateTime? staleExpiresAtUtc, CancellationToken cancellationToken)
    {
        await repository.LockTicketAsync(certificateId, service, cancellationToken);
        var ticket = await repository.GetTicketAsync(certificateId, service, cancellationToken);
        if (ticket is null)
        {
            ticket = ArcaAccessTicket.Create(certificateId, service);
            repository.Add(ticket);
        }

        var lease = ticket.TryLease(owner, timeProvider.GetUtcNow().UtcDateTime, margin, leaseDuration, staleExpiresAtUtc);
        return new ArcaTicketLeaseResponse(lease, ticket.ProtectedCredentials, ticket.ExpiresAtUtc, ticket.BlockedUntilUtc);
    }

    private async Task<Result<bool>> ChangeCoreAsync(Guid certificateId, ArcaServiceCode service, Func<ArcaAccessTicket, bool> change,
        CancellationToken cancellationToken)
    {
        await repository.LockTicketAsync(certificateId, service, cancellationToken);
        return await repository.GetTicketAsync(certificateId, service, cancellationToken) is { } ticket && change(ticket);
    }
}
