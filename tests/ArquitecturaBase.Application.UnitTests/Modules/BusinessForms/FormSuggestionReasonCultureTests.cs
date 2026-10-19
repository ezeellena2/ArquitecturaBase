using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormSuggestionReasonCultureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_Always_reason_keeps_the_conversation_language_when_the_complete_target_uses_its_original_language(bool forSending)
    {
        var fixture = new FormSuggestionRuntimeFixture();
        var ct = TestContext.Current.CancellationToken;
        await fixture.Assistant.Users.UpdateProfileAsync(fixture.Assistant.UserId, null, "en", "America/Argentina/Buenos_Aires", ct);
        var names = new Dictionary<string, string> { ["es"] = "Personas" };
        Assert.True(fixture.Assistant.Catalog.Catalog.UpdateManual(1, names, names).IsSuccess);

        var result = await fixture.Policy.PrepareAsync(fixture.Request with { Culture = "en", ForSending = forSending }, ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("es", result.Value.Prefill.Culture);
        var field = Assert.Single(result.Value.Prefill.Fields);
        Assert.Equal("Personas", field.Label);
        Assert.Equal("Ana", field.DisplayValue);
        Assert.Equal("Suggested after VISIT-000012.", result.Value.Prefill.Reason);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_untranslated_later_rule_makes_Always_use_the_original_form_and_catalog(bool forSending)
    {
        var fixture = new FormSuggestionRuntimeFixture();
        var ct = TestContext.Current.CancellationToken;
        await fixture.Assistant.Users.UpdateProfileAsync(fixture.Assistant.UserId, null, "en", "America/Argentina/Buenos_Aires", ct);
        fixture.Assistant.Catalog.SetRules([new(Guid.NewGuid(), "Approved", [],
            [new(Guid.NewGuid(), "NotifyAuthor", null, new Dictionary<string, string> { ["es"] = "Aprobamos la visita" }, false)])]);
        var result = await fixture.Policy.PrepareAsync(fixture.Request with { Culture = "en", ForSending = forSending }, ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("es", result.Value.Prefill.Culture);
        Assert.Equal("Personas", Assert.Single(result.Value.Prefill.Catalogs).Label);
        Assert.Equal("Ana", Assert.Single(Assert.Single(result.Value.Prefill.Catalogs).Options).Text);
        Assert.Equal("Suggested after VISIT-000012.", result.Value.Prefill.Reason);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Assistant_uses_the_common_fallback_while_preserving_the_observed_conversation_reason(bool forSending)
    {
        var fixture = new FormSuggestionAssistantFixture();
        var ct = TestContext.Current.CancellationToken;
        await fixture.Runtime.Assistant.Users.UpdateProfileAsync(fixture.Runtime.Assistant.UserId, null, "en", "America/Argentina/Buenos_Aires", ct);
        fixture.Runtime.Assistant.Catalog.SetRules([new(Guid.NewGuid(), "Approved", [],
            [new(Guid.NewGuid(), "NotifyAuthor", null, new Dictionary<string, string> { ["es"] = "Aprobamos la visita" }, false)])]);
        var proposal = fixture.Proposal();
        var read = proposal.ObservedReads[1];
        proposal = proposal with
        {
            Payload = Assert.IsType<FormSuggestionAssistantRegistrationPayload>(proposal.Payload) with { Reason = "Requested a follow-up." },
            ObservedReads = [read with { Payload = Assert.IsType<FormSuggestionAssistantCandidatesPayload>(read.Payload) with { Culture = "en" } }]
        };
        var result = await fixture.Runtime.Policy.PrepareAsync(fixture.Runtime.Request with { Culture = "en", Proposal = proposal, ForSending = forSending }, ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("es", result.Value.Prefill.Culture);
        Assert.Equal("Ana", Assert.Single(Assert.Single(result.Value.Prefill.Catalogs).Options).Text);
        Assert.Equal("Requested a follow-up.", result.Value.Prefill.Reason);
    }
}
