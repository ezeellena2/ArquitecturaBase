using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed class FormActionBackgroundServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task One_poll_drains_the_batch_even_when_three_effects_are_uncertain_or_throw(bool throwOnStalled)
    {
        var probe = new FormActionWorkerProbe(throwOnStalled);
        await using var provider = new ServiceCollection().AddSingleton(probe)
            .AddScoped<IFormActionDispatchService, FormActionWorkerDispatch>()
            .AddScoped<IFormActionEffectService, FormActionWorkerEffect>().BuildServiceProvider();
        var clock = new FakeTimeProvider();
        var logger = new FakeLogger<FormActionBackgroundService>();
        using var worker = new FormActionBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), clock,
            Options.Create(new FormActionWorkerOptions()), logger);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await probe.BatchCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(probe.RunIds, probe.Reserved);
            Assert.All(probe.RunIds.Skip(3), id => Assert.Contains(id, probe.Completed));
            Assert.Equal(throwOnStalled ? 3 : 0, logger.Collector.GetSnapshot().Count);
            Assert.All(logger.Collector.GetSnapshot(), item => Assert.DoesNotContain("private-effect-data", item.Message, StringComparison.Ordinal));
        }
        finally { await worker.StopAsync(TestContext.Current.CancellationToken); }
    }
}
