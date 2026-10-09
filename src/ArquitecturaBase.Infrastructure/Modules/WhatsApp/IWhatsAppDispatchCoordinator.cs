using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

internal interface IWhatsAppDispatchCoordinator
{
    Task<IWhatsAppDispatchLease> AcquireAsync(PhoneNumber recipient, CancellationToken cancellationToken);
}

internal interface IWhatsAppDispatchLease : IAsyncDisposable
{
    CancellationToken LostOwnership { get; }
}
