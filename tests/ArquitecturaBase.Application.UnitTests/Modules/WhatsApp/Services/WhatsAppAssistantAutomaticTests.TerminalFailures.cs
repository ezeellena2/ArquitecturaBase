using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed partial class WhatsAppAssistantAutomaticTests
{
    [Theory]
    [InlineData("window")]
    [InlineData("inactive")]
    [InlineData("expired")]
    public async Task Permanent_automatic_source_failures_are_marked_without_creating_provider_work(string failure)
    {
        var host = await HostAsync(false);
        var now = host.Context.Clock.GetUtcNow().UtcDateTime;
        var message = WhatsAppMessage.Inbound(host.Context.Contact.Id, "wamid.permanent-source", WhatsAppMessageKind.Text,
            "Fictitious visit", null, now);
        host.Messages.Messages.Add(message);
        host.Reader.Source = new WhatsAppAssistantSourceRow(message.Id, host.Context.Contact.Id, message.Body!, now, now.AddDays(90))
            { WaMessageId = message.WaMessageId };
        if (failure == "window") host.Context.Clock.Advance(TimeSpan.FromHours(24));
        if (failure == "inactive") await host.Context.Users.SetActiveAsync(host.Context.Subject.Id, false, Ct);
        if (failure == "expired") host.Reader.Source = host.Reader.Source with { ContentExpiresAtUtc = now };
        var result = await Service(host).PrepareAsync(host.Context.Contact.Id, Ct);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.NotNull(message.AssistantProcessedAtUtc);
        Assert.Empty(host.Requests.Rows);
        Assert.Equal(0, host.Assistant.Submits);
    }

    [Fact]
    public async Task A_marked_source_cannot_be_accepted_again_without_an_existing_reference()
    {
        var host = await HostAsync(false);
        host.Reader.Source = host.Reader.Source! with { AssistantProcessedAtUtc = host.Context.Clock.GetUtcNow().UtcDateTime };
        var result = await Service(host).PrepareAsync(host.Context.Contact.Id, Ct);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Empty(host.Requests.Rows);
    }
}
