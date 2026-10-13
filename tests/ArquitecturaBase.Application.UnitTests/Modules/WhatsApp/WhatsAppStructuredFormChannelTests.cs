using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using Microsoft.Extensions.DependencyInjection;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;
public sealed class WhatsAppStructuredFormChannelTests
{
    [Fact]
    public async Task Registration_is_idempotent_and_local_check_does_not_depend_on_external_qualification()
    {
        using var provider = new ServiceCollection().AddSingleton<IWhatsAppFlowEnvironment, StubWhatsAppFlowEnvironment>().AddWhatsAppApplication().AddWhatsAppApplication()
            .AddSingleton<IWhatsAppFlowPublicationService, UnusedFlowPublicationServices>()
            .AddSingleton<IWhatsAppFlowStatusService, UnusedFlowPublicationServices>().BuildServiceProvider();
        using var scope = provider.CreateScope(); var channel = Assert.Single(scope.ServiceProvider.GetServices<IStructuredFormChannel>());
        var result = await channel.CheckAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal("phone", channel.ChannelId); Assert.True(result.IsSuccess);
        Assert.Equal("LocallyValidated", result.Value.Compatibility); Assert.Equal("Pending", result.Value.ExternalEvidence);
        Assert.Equal(new[] { "en", "es" }, result.Value.Cultures.Select(culture => culture.Culture));
        Assert.All(result.Value.Cultures, culture => Assert.Equal(64, culture.CompilationFingerprint.Length));
    }
    [Fact]
    public async Task Cancelled_local_check_does_not_compile()
    {
        using var cancellation = new CancellationTokenSource(); await cancellation.CancelAsync();
        var channel = new WhatsAppStructuredFormChannel(new StubWhatsAppFlowEnvironment(), new UnusedFlowPublicationServices(), new UnusedFlowPublicationServices());
        await Assert.ThrowsAsync<OperationCanceledException>(() => channel.CheckAsync(Request(), cancellation.Token));
    }

    [Fact]
    public void Publication_request_fingerprint_is_versioned_canonical_and_reads_legacy_manifests()
    {
        var request = PublicationRequest();
        var reordered = request with
        {
            RequiredPermissions = request.RequiredPermissions.Reverse().ToArray(),
            Cultures = request.Cultures.Reverse().ToArray(),
            Snapshot = request.Snapshot with
            {
                Name = new Dictionary<string, string> { ["en"] = "Form", ["es"] = "Formulario" },
                SubmitLabel = new Dictionary<string, string> { ["en"] = "Submit", ["es"] = "Enviar" },
            },
        };

        var fingerprint = WhatsAppFlowPublicationPolicy.RequestFingerprint(request);
        Assert.Equal(fingerprint, WhatsAppFlowPublicationPolicy.RequestFingerprint(reordered));
        Assert.NotEqual(fingerprint, WhatsAppFlowPublicationPolicy.RequestFingerprint(request with { PublicationFingerprint = "other" }));

        var account = new WhatsAppFlowAccountContext("waba", "phone", "v25.0", "configuration");
        var legacy = new WhatsAppFlowStoredManifest(1, WhatsAppFlowPublicationPolicy.Hash(request), "publication", account, ["permission"]);
        var current = new WhatsAppFlowStoredManifest(2, fingerprint, "publication", account, ["permission"]);
        Assert.True(WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(legacy, request));
        Assert.True(WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(current, reordered));
        Assert.False(WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(current, request with { ActorUserId = Guid.NewGuid() }));
    }

    [Fact]
    public void Legacy_publication_recovers_after_target_culture_order_changed_without_accepting_payload_changes()
    {
        var original = PublicationRequest();
        var account = new WhatsAppFlowAccountContext("waba", "phone", "v25.0", "configuration");
        var manifest = new WhatsAppFlowStoredManifest(1, WhatsAppFlowPublicationPolicy.Hash(original),
            original.PublicationFingerprint, account, original.RequiredPermissions);
        var recovered = original with { Snapshot = original.Snapshot with { TargetCultures = ["en", "es"] } };

        Assert.True(WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(manifest, recovered));
        Assert.False(WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(manifest, recovered with { ActorUserId = Guid.NewGuid() }));
        Assert.False(WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(manifest, recovered with { PublicationFingerprint = "changed" }));
        Assert.False(WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(manifest, recovered with { RequiredPermissions = ["changed"] }));
        Assert.False(WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(manifest, recovered with
        {
            Snapshot = recovered.Snapshot with { Name = new Dictionary<string, string> { ["es"] = "Otro", ["en"] = "Other" } },
        }));
        Assert.False(WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(manifest, recovered with
        {
            Snapshot = recovered.Snapshot with { TargetCultures = ["en", "es", "es"] },
        }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_permanent_acceptance_rejection_is_failed_without_reconciliation(bool unsupported)
    {
        var error = unsupported ? FlowErrors.UnsupportedConfiguration : FlowErrors.PayloadMismatch;
        var channel = new WhatsAppStructuredFormChannel(new StubWhatsAppFlowEnvironment(),
            new UnusedFlowPublicationServices(error), new UnusedFlowPublicationServices());
        var request = PublicationRequest();
        var result = await channel.AcceptAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal("Failed", result.Value.State);
        Assert.Equal(error.Code, result.Value.Stage);
        Assert.False(result.Value.RequiresReview);
        Assert.Null(result.Value.NextCheckAtUtc);
        Assert.Equal(request.OperationId, result.Value.OperationId);
    }

    [Fact]
    public async Task An_unavailable_account_remains_recoverable_instead_of_being_a_permanent_rejection()
    {
        var channel = new WhatsAppStructuredFormChannel(new StubWhatsAppFlowEnvironment(),
            new UnusedFlowPublicationServices(FlowErrors.AccountUnavailable), new UnusedFlowPublicationServices());
        var result = await channel.AcceptAsync(PublicationRequest(), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(FlowErrors.AccountUnavailable, result.Error);
    }

    private static StructuredFormPublicationRequest PublicationRequest() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        ["permission", "other"], "publication", "profile",
        [new("es", "es-hash"), new("en", "en-hash")], Request());

    private static StructuredFormCheckRequest Request() => new(1, "es", new Dictionary<string, string> { ["es"] = "Formulario", ["en"] = "Form" }, new Dictionary<string, string> { ["es"] = "Enviar", ["en"] = "Submit" }, [], ["es", "en"]);
}
