using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

/// <summary>H19: a message and a send keep, once, the W3C trace context where they were written.</summary>
public sealed class WhatsAppTraceTests
{
    private const string First = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";
    private const string Second = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    private static readonly DateTime Now = new(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_message_keeps_the_first_context()
    {
        var message = WhatsAppMessage.Inbound(Guid.NewGuid(), "wamid.trace", WhatsAppMessageKind.Text, "Hola", null, Now);
        Assert.Null(message.TraceParent);

        message.RecordTrace(First);
        message.RecordTrace(Second);

        Assert.Equal(First, message.TraceParent);
    }

    [Fact]
    public void A_send_keeps_the_first_context()
    {
        var outgoing = Send();

        outgoing.RecordTrace(First);
        outgoing.RecordTrace(Second);

        Assert.Equal(First, outgoing.TraceParent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("|4bf92f3577b34da6a3ce929d0e0e4736.00f067aa0ba902b7.")]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01-extra")]
    public void Only_a_w3c_context_fits(string trace)
    {
        var outgoing = Send();

        Assert.Throws<ArgumentException>(() => outgoing.RecordTrace(trace));
        Assert.Null(outgoing.TraceParent);
    }

    private static WhatsAppOutgoingMessage Send() => WhatsAppOutgoingMessage.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Hola", Now);
}
