using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
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
    public const string Cuit = "20000000060";

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

    /// <summary>
    /// La conexión con su certificado activo, emitido por la CA de prueba para un pedido real: lo que necesita el WSAA
    /// para firmar el pedido de ticket.
    /// </summary>
    public static async Task<ArcaCertificateRef> AddActiveCertificateAsync(ApiFactory factory, FakeArcaCertificateAuthority authority)
    {
        var connectionId = await AddConnectionAsync(factory);
        var certificateId = Guid.CreateVersion7();
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        var credentials = factory.Services.GetRequiredService<IArcaCredentialService>();
        var request = credentials.CreateRequest(connectionId, certificateId, Cuit, "plataforma");
        var issued = authority.Issue(request.RequestPem, ArcaEnvironment.Testing, nowUtc.AddYears(1));
        var inspected = credentials.Inspect(connectionId, certificateId, request.ProtectedPrivateKey, issued, Cuit, ArcaEnvironment.Testing, nowUtc).Value;
        await factory.InTransactionAsync(services =>
        {
            var certificate = ArcaCertificate.Request(certificateId, connectionId, "plataforma", request.RequestPem, request.ProtectedPrivateKey).Value;
            certificate.Load(inspected.CertificatePem, inspected.NotBeforeUtc, inspected.NotAfterUtc, inspected.Thumbprint);
            certificate.Promote();
            services.GetRequiredService<IArcaConnectionRepository>().Add(certificate);
            return Task.CompletedTask;
        });
        return new ArcaCertificateRef(connectionId, certificateId, Cuit, ArcaEnvironment.Testing, request.ProtectedPrivateKey, inspected.CertificatePem);
    }

    public static Task RemoveConnectionAsync(ApiFactory factory) => factory.InTransactionAsync(async services =>
    {
        var repository = services.GetRequiredService<IArcaConnectionRepository>();
        if (await repository.GetConnectionAsync(TestContext.Current.CancellationToken) is { } connection) repository.Remove(connection);
    });
}
