using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;

/// <summary>
/// Arma la conexión de la instalación como lo haría un caso de uso, dentro de un límite real. Hay una sola por base: cada
/// test la borra al terminar, y el borrado en cascada se lleva lo demás.
/// </summary>
internal static class ArcaPersistenceScenario
{
    public static readonly ArcaServiceCode Invoicing = ArcaServiceCode.Create("wsfe")!.Value;
    public static readonly ArcaServiceCode Registry = ArcaServiceCode.Create("ws_sr_constancia_inscripcion")!.Value;

    public static ArcaConnection NewConnection() =>
        ArcaConnection.Start("20-00000006-0", ArcaEnvironment.Testing, [Invoicing], [Invoicing, Registry], [Registry]).Value;

    public static ArcaCertificate NewCertificate(Guid connectionId, string alias = "plataforma") =>
        ArcaCertificate.Request(Guid.CreateVersion7(), connectionId, alias, "-----BEGIN CERTIFICATE REQUEST-----", "protected-key").Value;

    public static async Task<Guid> AddConnectionAsync(ApiFactory factory)
    {
        var connection = NewConnection();
        await factory.InTransactionAsync(services =>
        {
            services.GetRequiredService<IArcaConnectionRepository>().Add(connection);
            return Task.CompletedTask;
        });
        return connection.Id;
    }

    public static Task RemoveConnectionAsync(ApiFactory factory) => factory.InTransactionAsync(async services =>
    {
        var repository = services.GetRequiredService<IArcaConnectionRepository>();
        if (await repository.GetConnectionAsync(TestContext.Current.CancellationToken) is { } connection) repository.Remove(connection);
    });
}
