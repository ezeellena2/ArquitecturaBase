using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
public sealed class WhatsAppFlowReplyRetryTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 90)]
    [InlineData(3, 210)]
    [InlineData(4, 450)]
    [InlineData(5, 930)]
    public void Recent_failures_do_not_take_slots_before_their_next_due_time(int failures, int seconds)
    {
        var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        var message = WhatsAppMessage.Inbound(Guid.NewGuid(), "wamid.retry", WhatsAppMessageKind.FlowReply, null, null, now, [1]);
        Assert.True(WhatsAppFlowSessionReader.RetryDueAt(now).Compile()(message));
        for (var index = 0; index < failures; index++) message.RegisterFlowReplyFailure(now);
        Assert.False(WhatsAppFlowSessionReader.RetryDueAt(now.AddSeconds(seconds - 1)).Compile()(message));
        Assert.True(WhatsAppFlowSessionReader.RetryDueAt(now.AddSeconds(seconds)).Compile()(message));
    }
}
