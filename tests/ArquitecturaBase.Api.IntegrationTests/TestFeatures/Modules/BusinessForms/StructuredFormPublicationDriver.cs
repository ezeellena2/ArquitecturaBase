using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed class StructuredFormPublicationDriver(FormPublicationBackgroundService worker) : IStructuredFormPublicationDriver
{
    public Task ProcessAsync(Guid operationId, CancellationToken cancellationToken) => worker.ProcessNextAsync(operationId, cancellationToken);
}
