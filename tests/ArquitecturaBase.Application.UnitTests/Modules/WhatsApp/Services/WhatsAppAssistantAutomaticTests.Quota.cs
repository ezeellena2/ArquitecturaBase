using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed partial class WhatsAppAssistantAutomaticTests
{
    [Fact]
    public async Task Exhausted_offer_quota_blocks_a_new_interpretation_before_creating_provider_work()
    {
        var host = await HostAsync(false); host.Reader.OfferCount = 2;
        var service = Service(host);
        var result = await service.PrepareAsync(host.Context.Contact.Id, Ct);
        Assert.True(result.IsSuccess); Assert.Null(result.Value);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked, Assert.Single(host.Requests.Rows).State);
        Assert.Equal(0, host.Assistant.Submits);
        Assert.Null((await service.PrepareAsync(host.Context.Contact.Id, Ct)).Value);
        Assert.Single(host.Requests.Rows);
    }
    [Fact]
    public async Task Exhausted_quota_also_blocks_a_new_interpreted_followup_after_capture()
    {
        var fixture = await WhatsAppConfiguredDispatchFixture.CreateAsync(true);
        var candidate = fixture.Host.Forms.Suggestions.Value.Candidates[0] with { Mode = "Assistant", When = "Another visit is needed" };
        fixture.Host.Forms.Suggestions = fixture.Host.Forms.Suggestions.Value with { Candidates = [candidate] };
        fixture.Host.Reader.OfferCount = 2;
        var result = await fixture.Service.PrepareAsync(fixture.Host.Context.Contact.Id, Ct);
        Assert.True(result.IsSuccess); Assert.Null(result.Value);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked, Assert.Single(fixture.Host.Requests.Rows).State);
        Assert.Equal(0, fixture.Host.Assistant.Submits);
    }
    [Fact]
    public async Task An_unaccepted_reference_rejected_during_recovery_is_durably_blocked()
    {
        var host = await HostAsync(false); var service = Service(host);
        var first = await service.PrepareAsync(host.Context.Contact.Id, Ct);
        host.Reader.OfferCount = 2;
        var recovered = await service.PrepareAsync(host.Context.Contact.Id, Ct);
        Assert.True(recovered.Value!.RecoverOnly);
        Assert.True((await service.CompleteAsync(host.Context.Contact.Id, first.Value!.RequestId,
            ArquitecturaBase.Domain.Results.Result.Failure<ArquitecturaBase.Application.Models.Assistant.AssistantOperationResponse>(WhatsAppAssistantErrors.StateConflict), Ct)).IsSuccess);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked, Assert.Single(host.Requests.Rows).State);
        Assert.Equal(0, host.Assistant.Submits);
    }
}
