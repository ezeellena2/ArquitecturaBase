using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Soap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection;

/// <summary>
/// Coordina el ticket compartido como el coordinador del asistente de IA: cada paso guardado corre en un scope propio,
/// con su DbContext y su transacción, y el pedido al WSAA va entre uno y otro, sin transacción (ADR 0023). Quien no
/// consigue la reserva espera de a medio segundo, como mucho lo que dura una reserva, y vuelve a mirar.
/// </summary>
internal sealed class ArcaAccessTicketProvider(IServiceScopeFactory scopes, ArcaAuthClient auth, IArcaCredentialService credentials,
    IOptions<ArcaOptions> options, TimeProvider timeProvider) : IArcaAccessTicketProvider
{
    private static readonly TimeSpan BusyPoll = TimeSpan.FromMilliseconds(500);

    public async Task<ArcaTicketOutcome> GetAsync(ArcaCertificateRef certificate, ArcaServiceCode service, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var owner = Guid.NewGuid();
        var deadline = timeProvider.GetUtcNow() + settings.TicketLease;
        while (true)
        {
            var lease = await InScopeAsync(store =>
                store.LeaseAsync(certificate.Id, service, owner, settings.TicketMargin, settings.TicketLease, cancellationToken));
            if (lease.Lease == ArcaTicketLease.AlreadyValid) return Stored(certificate, service, lease);
            if (lease.Lease == ArcaTicketLease.Blocked) return new ArcaTicketOutcome.Blocked(lease.BlockedUntilUtc!.Value);
            if (lease.Lease == ArcaTicketLease.Busy)
            {
                if (timeProvider.GetUtcNow() >= deadline) return new ArcaTicketOutcome.Unavailable();
                await Task.Delay(BusyPoll, timeProvider, cancellationToken);
                continue;
            }

            var outcome = await auth.LoginAsync(certificate, service, cancellationToken);
            await InScopeAsync(store => outcome switch
            {
                ArcaTicketOutcome.Ready ready => store.StoreAsync(certificate.Id, service, owner,
                    credentials.ProtectTicket(certificate.ConnectionId, certificate.Id, service, ready.Token, ready.Sign), ready.ExpiresAtUtc, cancellationToken),
                ArcaTicketOutcome.Blocked blocked => store.BlockAsync(certificate.Id, service, owner, blocked.UntilUtc, cancellationToken),
                _ => store.ReleaseAsync(certificate.Id, service, owner, cancellationToken),
            });
            return outcome;
        }
    }

    private ArcaTicketOutcome Stored(ArcaCertificateRef certificate, ArcaServiceCode service, ArcaTicketLeaseResponse lease) =>
        lease.ProtectedCredentials is { } stored
        && credentials.UnprotectTicket(certificate.ConnectionId, certificate.Id, service, stored) is (var token, var sign)
            ? new ArcaTicketOutcome.Ready(token, sign, lease.ExpiresAtUtc!.Value)
            : new ArcaTicketOutcome.Unavailable();

    private async Task<T> InScopeAsync<T>(Func<IArcaAccessTicketStore, Task<T>> step)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await step(scope.ServiceProvider.GetRequiredService<IArcaAccessTicketStore>());
    }
}
