using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class PublicationWorkerLoggingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Form_publication_worker_logs_exception_type_without_exception_message()
    {
        const string secretMarker = "access_token=secret-value";
        await using var provider = new ServiceCollection()
            .AddSingleton<IFormPublicationDispatchService>(new ThrowingFormPublicationDispatch(secretMarker))
            .BuildServiceProvider();
        var logger = new FakeLogger<FormPublicationBackgroundService>();
        var worker = new FormPublicationBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System, Options.Create(new FormPublicationWorkerOptions()), logger);

        await worker.StartAsync(Ct);
        await WaitForLogAsync(logger, Ct);
        await worker.StopAsync(Ct);

        var message = Assert.Single(logger.Collector.GetSnapshot()).Message;
        Assert.Contains(nameof(InvalidOperationException), message, StringComparison.Ordinal);
        Assert.DoesNotContain(secretMarker, message, StringComparison.Ordinal);
    }

    private static async Task WaitForLogAsync<T>(FakeLogger<T> logger, CancellationToken cancellationToken) where T : class
    {
        for (var attempt = 0; attempt < 100 && logger.Collector.GetSnapshot().Count == 0; attempt++)
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        Assert.NotEmpty(logger.Collector.GetSnapshot());
    }

    private sealed class ThrowingFormPublicationDispatch(string message) : IFormPublicationDispatchService
    {
        public Task<IReadOnlyList<Guid>> ListDueOperationIdsAsync(CancellationToken cancellationToken) => throw new InvalidOperationException(message);

        public Task<Result<FormPublicationWorkResponse?>> ReserveAsync(Guid? operationId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);

        public Task<Result> CompleteAsync(FormPublicationWorkResponse work, IReadOnlyList<FormPublicationChannelProgress> progress,
            CancellationToken cancellationToken) => throw new NotImplementedException();
    }

}
