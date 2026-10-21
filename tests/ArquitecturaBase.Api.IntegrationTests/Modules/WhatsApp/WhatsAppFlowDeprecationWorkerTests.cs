using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppFlowDeprecationWorkerTests
{
    [Fact]
    public async Task A_post_is_marked_before_the_provider_and_completed_even_after_host_cancellation()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var fake = new WhatsAppFlowDeprecationWorkerFixture { CancelAfterPost = stop };
        await RunAsync(fake, stop.Token);
        Assert.Equal(["Reserve", "Account", "Mark", "POST", "Complete"], fake.Events);
        Assert.True(fake.Effect!.Success); Assert.False(fake.CompletionWasCancelled);
    }
    [Fact]
    public async Task An_unknown_prior_effect_is_observed_without_marking_or_posting()
    {
        var fake = new WhatsAppFlowDeprecationWorkerFixture { Verify = true };
        await RunAsync(fake, TestContext.Current.CancellationToken);
        Assert.Equal(["Reserve", "Account", "GET", "Verified"], fake.Events);
        Assert.Equal(WhatsAppFlowRemoteState.Deprecated, fake.Verification!.State);
    }
    [Fact]
    public async Task Cancellation_after_the_post_begins_is_completed_as_uncertain()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var fake = new WhatsAppFlowDeprecationWorkerFixture { CancelAfterPost = stop, ThrowAfterPost = true };
        await RunAsync(fake, stop.Token);
        Assert.True(fake.Effect!.Uncertain); Assert.False(fake.CompletionWasCancelled);
    }
    [Fact]
    public async Task A_different_current_account_never_marks_or_posts_the_sealed_flow()
    {
        var fake = new WhatsAppFlowDeprecationWorkerFixture { DifferentAccount = true };
        await RunAsync(fake, TestContext.Current.CancellationToken);
        Assert.Equal(["Reserve", "Account", "Complete"], fake.Events);
        Assert.Equal(FlowErrors.AccountMismatch.Code, fake.Effect!.FailureCode);
    }
    private static async Task RunAsync(WhatsAppFlowDeprecationWorkerFixture fake, CancellationToken ct)
    {
        await using var provider = new ServiceCollection().AddSingleton<IWhatsAppFlowDeprecationService>(fake)
            .AddSingleton<IWhatsAppFlowEnvironment>(fake).AddSingleton<IWhatsAppFlowProvider>(fake).BuildServiceProvider();
        var worker = new WhatsAppFlowDeprecationBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WhatsAppOptions()), fake.Clock, NullLogger<WhatsAppFlowDeprecationBackgroundService>.Instance);
        await worker.ProcessNextAsync(Guid.NewGuid(), ct);
    }
}
