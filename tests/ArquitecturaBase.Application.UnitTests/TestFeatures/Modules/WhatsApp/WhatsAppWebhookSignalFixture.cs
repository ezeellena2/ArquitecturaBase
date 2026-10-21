using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppWebhookSignalFixture : IWhatsAppSignatureValidator, IWhatsAppWebhookReader,
    IWhatsAppWebhookPersistence, IWhatsAppWebhookRetry
{
    public WhatsAppWebhookBatch Batch { get; set; } = WhatsAppWebhookBatch.Empty;
    public bool SignatureValid { get; set; } = true;
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeUnitOfWork RetryUnitOfWork { get; } = new();
    public List<string> Events { get; } = [];
    public int ReadCalls { get; private set; }
    public int PersistCalls { get; private set; }
    public int RetryCalls { get; private set; }
    public int SuccessfulCommits { get; private set; }
    public IWhatsAppInboundSignal InboundSignal { get; }
    public IWhatsAppFlowReplySignal FlowReplySignal { get; }

    public WhatsAppWebhookSignalFixture()
    {
        InboundSignal = new InboundNotice(this);
        FlowReplySignal = new FlowReplyNotice(this);
    }

    public Task<bool> ReceiveAsync(CancellationToken cancellationToken) =>
        new WhatsAppWebhookService(this, this, this, this, InboundSignal, FlowReplySignal,
            NullLogger<WhatsAppWebhookService>.Instance).ReceiveAsync("{}"u8.ToArray(), "test-signature", cancellationToken);

    public bool IsValidVerifyToken(string? verifyToken) => false;
    public bool IsValidSignature(ReadOnlySpan<byte> body, string? signatureHeader) => SignatureValid;
    public WhatsAppWebhookBatch Read(ReadOnlyMemory<byte> body)
    {
        ReadCalls++;
        return Batch;
    }

    public Task PersistAsync(WhatsAppWebhookBatch batch, CancellationToken cancellationToken)
    {
        PersistCalls++;
        return CommitAsync(UnitOfWork, batch, cancellationToken);
    }

    public Task RetryAsync(WhatsAppWebhookBatch batch, CancellationToken cancellationToken)
    {
        Assert.False(UnitOfWork.InTransaction);
        RetryCalls++;
        return CommitAsync(RetryUnitOfWork, batch, cancellationToken);
    }

    private async Task CommitAsync(FakeUnitOfWork unitOfWork, WhatsAppWebhookBatch batch, CancellationToken cancellationToken)
    {
        Assert.Same(Batch, batch);
        if (batch.IsEmpty) return;
        await unitOfWork.ExecuteInTransactionAsync(_ => Task.FromResult(Result.Success()),
            CommitPolicy.OnSuccess, cancellationToken);
        SuccessfulCommits++;
        Events.Add("commit");
    }

    private void Notify(string signal)
    {
        Assert.False(UnitOfWork.InTransaction);
        Assert.False(RetryUnitOfWork.InTransaction);
        Assert.True(SuccessfulCommits > 0, "A webhook signal must follow a completed commit.");
        Events.Add(signal);
    }

    private sealed class InboundNotice(WhatsAppWebhookSignalFixture owner) : IWhatsAppInboundSignal
    {
        public void Notify() => owner.Notify("inbound");
    }

    private sealed class FlowReplyNotice(WhatsAppWebhookSignalFixture owner) : IWhatsAppFlowReplySignal
    {
        public void Notify() => owner.Notify("flow");
    }
}
