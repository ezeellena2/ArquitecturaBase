using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFlowPublicationLanguageTests
{
    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task Acceptance_seals_only_the_original_flow_when_secondary_texts_are_incomplete(string original, string secondary)
    {
        var fixture = new WhatsAppFlowPublicationFixture();
        var request = Request(original, [original]);
        var texts = new Dictionary<string, string> { [original] = "Original", [secondary] = "" };
        request = request with { Snapshot = request.Snapshot with { Name = texts } };

        var result = await fixture.CreateService().AcceptAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Accepted", result.Value.State);
        Assert.Equal(original, Assert.Single(fixture.Bindings).Culture);
        Assert.Equal(original, Assert.Single(fixture.Projections).Value.Culture);
        Assert.Equal(1, fixture.AccountChecks);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
    }

    [Theory]
    [InlineData("es", "es", "es")]
    [InlineData("en", "en", "en")]
    [InlineData("es", "es", "fr")]
    [InlineData("en", "en", "fr")]
    [InlineData("es", "en", null)]
    [InlineData("en", "es", null)]
    public async Task Invalid_targets_are_rejected_before_account_verification_or_writes(string original, string first, string? second)
    {
        var fixture = new WhatsAppFlowPublicationFixture();
        var targets = second is null ? new[] { first } : new[] { first, second };
        var request = Request(original, targets);

        var result = await fixture.CreateService().AcceptAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(FlowErrors.UnsupportedConfiguration, result.Error);
        Assert.Empty(fixture.Bindings);
        Assert.Equal(0, fixture.AccountChecks);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }

    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public void Compilation_uses_only_the_requested_original_without_filling_secondary_texts(string original, string secondary)
    {
        var request = Request(original, [original]);
        var compilation = WhatsAppFlowCompiler.Compile(request.Snapshot);

        Assert.Empty(compilation.Issues);
        var flow = Assert.Single(compilation.Flows);
        Assert.Equal(original, flow.Culture);
        Assert.DoesNotContain(secondary, request.Snapshot.Name.Keys);
        Assert.Contains("Original", flow.Json, StringComparison.Ordinal);
    }

    private static StructuredFormPublicationRequest Request(string original, IReadOnlyList<string> targets)
    {
        var text = new Dictionary<string, string> { [original] = "Original" };
        var snapshot = new StructuredFormCheckRequest(1, original, text, text, [], targets);
        var compilation = WhatsAppFlowCompiler.Compile(snapshot);
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ["forms.read"], "publication",
            WhatsAppFlowCompiler.ProfileRevision, compilation.Flows.Select(flow => new StructuredFormPublicationCulture(
                flow.Culture, WhatsAppFlowPublicationPolicy.CompilationFingerprint(flow.Hash))).ToArray(), snapshot);
    }
}
