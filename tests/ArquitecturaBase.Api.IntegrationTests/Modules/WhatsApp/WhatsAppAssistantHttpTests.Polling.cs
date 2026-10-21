using System.Net;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppAssistantHttpTests
{
    [Fact]
    public async Task Fast_polling_tracks_live_pending_references_instead_of_new_sources_or_finished_proposals()
    {
        await using var host=await WhatsAppAssistantScenario.CreateAsync(factory,Ct);
        using var client=host.Client(host.FirstOperator);
        var contact=await host.ContactAsync(Ct);
        var now=host.Clock.GetUtcNow().UtcDateTime;
        Assert.False(await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().ExistsPendingAsync(now,Ct)));

        var requestId=Guid.NewGuid();
        using var accepted=await RequestAsync(client,contact,requestId);
        Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
        Assert.True(await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().ExistsPendingAsync(now,Ct)));
        await host.CompleteAsync(contact,requestId,"ready",Ct);
        Assert.False(await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().ExistsPendingAsync(now,Ct)));

        var expiring=await host.ContactAsync(Ct);
        var expiringId=Guid.NewGuid();
        using var pending=await RequestAsync(client,expiring,expiringId);
        Assert.Equal(HttpStatusCode.Accepted,pending.StatusCode);
        var reference=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().FindAsync(expiring.ContactId,expiringId,Ct));
        Assert.NotNull(reference);
        Assert.True(await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().ExistsPendingAsync(now,Ct)));
        host.Clock.SetUtcNow(new DateTimeOffset(reference.ContentExpiresAtUtc));
        Assert.False(await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().ExistsPendingAsync(host.Clock.GetUtcNow().UtcDateTime,Ct)));
    }
}