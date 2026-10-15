namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

internal interface IStructuredFormPublicationDriver
{
    Task ProcessAsync(Guid operationId, CancellationToken cancellationToken);
}
