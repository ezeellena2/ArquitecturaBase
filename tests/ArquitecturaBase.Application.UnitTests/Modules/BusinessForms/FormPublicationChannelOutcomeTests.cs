using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormPublicationChannelOutcomeTests
{
    [Theory]
    [InlineData("Failed", false, false, "Failed", 1, 0, false)]
    [InlineData("Accepted", true, false, "Failed", 0, 1, false)]
    [InlineData("Accepted", false, true, "Uncertain", 1, 0, true)]
    [InlineData("Accepted", false, false, "Uncertain", 1, 1, true)]
    public async Task Explicit_no_effect_results_close_the_attempt_but_unknown_results_preserve_review(
        string acceptanceState, bool reconciliationOnly, bool failAcceptance, string expectedState,
        int acceptCalls, int lookupCalls, bool requiresReview)
    {
        var channel = new PublicationOutcomeChannel(acceptanceState, failAcceptance, lookupMissing: true);
        var service = new FormPublicationChannelDispatchService([channel], NullLogger<FormPublicationChannelDispatchService>.Instance);

        var result = await service.DispatchAsync(Work(reconciliationOnly), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var progress = Assert.Single(result.Value.Progress);
        Assert.Equal(expectedState, progress.Response.State);
        Assert.Equal(requiresReview, progress.Response.RequiresReview);
        Assert.Equal(acceptCalls, channel.AcceptCalls);
        Assert.Equal(lookupCalls, channel.LookupCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_missing_channel_cannot_prove_that_an_external_effect_does_not_exist(bool reconciliationOnly)
    {
        var service = new FormPublicationChannelDispatchService([], NullLogger<FormPublicationChannelDispatchService>.Instance);

        var result = await service.DispatchAsync(Work(reconciliationOnly), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var response = Assert.Single(result.Value.Progress).Response;
        Assert.Equal("Uncertain", response.State);
        Assert.True(response.RequiresReview);
    }

    private static FormPublicationWorkResponse Work(bool reconciliationOnly)
    {
        var operationId = Guid.NewGuid(); var definitionId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var texts = new Dictionary<string, string> { ["es"] = "Prueba", ["en"] = "Test" };
        var snapshot = new StructuredFormCheckRequest(1, "es", texts, texts, [], ["es", "en"]);
        var request = new StructuredFormPublicationRequest(operationId, definitionId, versionId, actorId,
            ["formtemplates.publish"], "publication-fingerprint", "test-profile", [], snapshot);
        var manifest = new FormPublicationManifest(1, "attempt-fingerprint", "publication-fingerprint", definitionId,
            versionId, 1, "content-hash", texts, "TEST", "es", texts, []);
        return new(operationId, definitionId, versionId, actorId, 1, manifest, new FormVersionSchema(1, []),
            [new("outcome-test", request)], reconciliationOnly);
    }
}
