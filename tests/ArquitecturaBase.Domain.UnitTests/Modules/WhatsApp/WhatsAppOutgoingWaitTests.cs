using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

/// <summary>
/// H25: a send of a kind this version does not know waits, in its place, for a copy that knows it, up to a limit since its request
/// and without spending the retries of its send.
/// </summary>
public sealed class WhatsAppOutgoingWaitTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(10);

    [Fact]
    public void A_pending_send_waits_without_spending_its_retries_until_its_limit()
    {
        var outgoing = WhatsAppOutgoingMessage.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Hola", Now);

        Assert.True(outgoing.WaitForAnotherCopy(Now.AddMinutes(1), Wait, Limit));
        Assert.Equal((WhatsAppOutgoingMessageState.Pending, (DateTime?)Now.AddMinutes(1).Add(Wait), 0),
            (outgoing.State, outgoing.NextAttemptAtUtc, outgoing.DispatchRetries));
        Assert.True(outgoing.WaitForAnotherCopy(Now.AddMinutes(9), Wait, Limit));
        Assert.False(outgoing.WaitForAnotherCopy(Now.AddMinutes(10), Wait, Limit));
        Assert.Equal(((DateTime?)Now.AddMinutes(9).Add(Wait), 0), (outgoing.NextAttemptAtUtc, outgoing.DispatchRetries));
    }

    [Fact]
    public void Only_a_pending_send_waits()
    {
        var outgoing = WhatsAppOutgoingMessage.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Hola", Now);
        outgoing.Start(Now);

        Assert.False(outgoing.WaitForAnotherCopy(Now, Wait, Limit));
        Assert.Equal((WhatsAppOutgoingMessageState.Sending, (DateTime?)null), (outgoing.State, outgoing.NextAttemptAtUtc));
    }
}
