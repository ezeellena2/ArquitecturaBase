using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class FormSendFailureAdapters(string phase) : IWhatsAppCloudClient,
    IWhatsAppDispatchCoordinator, IWhatsAppFormMessageDispatchService
{
    public int ProviderCalls { get; private set; }

    public Task<Result<WhatsAppFlowMessageResponse>> PrepareAsync(Guid outgoingId, CancellationToken cancellationToken) =>
        Task.FromException<Result<WhatsAppFlowMessageResponse>>(new InvalidOperationException("Preparation failed."));

    public Task<IWhatsAppDispatchLease> AcquireAsync(PhoneNumber recipient, CancellationToken cancellationToken) =>
        phase == "Lease"
            ? Task.FromException<IWhatsAppDispatchLease>(new InvalidOperationException("Lease acquisition failed."))
            : Task.FromResult<IWhatsAppDispatchLease>(new Lease());

    public Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        ProviderCalls++;
        return Task.FromException<WhatsAppSendResult>(new InvalidOperationException("The provider outcome is unknown."));
    }

    private sealed class Lease : IWhatsAppDispatchLease
    {
        public CancellationToken LostOwnership => CancellationToken.None;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
