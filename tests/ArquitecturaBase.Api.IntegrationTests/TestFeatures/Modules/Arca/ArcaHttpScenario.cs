using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;

/// <summary>
/// El alta de ARCA por HTTP, como la hace la pantalla: un operador con los permisos del módulo, el pedido, el certificado
/// emitido por la CA de prueba y la subida. Guarda cada cuerpo recibido para revisar que ninguno lleve secretos.
/// </summary>
internal sealed class ArcaHttpScenario(ApiFactory factory, FakeArcaCertificateAuthority authority) : IAsyncDisposable
{
    private readonly List<string> bodies = [];

    public HttpClient Client { get; private set; } = null!;

    public IReadOnlyList<string> Bodies => bodies;

    public static async Task<ArcaHttpScenario> CreateAsync(ApiFactory factory, FakeArcaCertificateAuthority authority)
    {
        var scenario = new ArcaHttpScenario(factory, authority);
        scenario.Client = await OperatorAsync(factory, ArcaPermissions.All);
        factory.Services.GetRequiredService<FakeArca>().Reset();
        return scenario;
    }

    public static async Task<HttpClient> OperatorAsync(ApiFactory factory, IReadOnlyCollection<string> permissions)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await factory.InTransactionAsync(async services =>
        {
            var name = "arca-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(name, null, permissions, ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("arca-operator")).Value, null, false, null, "es", ct);
            await users.SetRolesAsync(user.Id, [name], ct);
            return user.Id;
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, id.ToString());
        return client;
    }

    public FakeArca Arca => factory.Services.GetRequiredService<FakeArca>();

    public async Task<JsonElement> GetConnectionAsync() => await ReadAsync(await Client.GetAsync("/api/arca/connection", Ct), HttpStatusCode.OK);

    public async Task<JsonElement> StartAsync(string[] services, string alias = "plataforma", long? expectedRevision = null)
    {
        var revision = expectedRevision ?? (await GetConnectionAsync()).GetProperty("revision").GetInt64();
        var body = new { cuit = "20-00000006-0", environment = "Testing", alias, services, expectedRevision = revision };
        return await ReadAsync(await Client.PostAsJsonAsync("/api/arca/connection/certificate-requests", body, Ct), HttpStatusCode.OK);
    }

    /// <summary>Baja el pedido pendiente, lo emite con la CA de prueba y lo sube: la Api prueba la conexión en el mismo paso.</summary>
    public async Task<JsonElement> UploadAsync()
    {
        using var request = await Client.GetAsync("/api/arca/connection/certificate-requests/current", Ct);
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        var pem = await request.Content.ReadAsStringAsync(Ct);
        var certificate = authority.Issue(pem, ArcaEnvironment.Testing, factory.Clock.GetUtcNow().UtcDateTime.AddYears(1));
        var revision = (await GetConnectionAsync()).GetProperty("revision").GetInt64();
        return await ReadAsync(await Client.PutAsJsonAsync("/api/arca/connection/certificate", new { certificate, expectedRevision = revision }, Ct),
            HttpStatusCode.OK);
    }

    /// <summary>El alta entera hasta el punto de venta elegido, si la facturación está entre los servicios.</summary>
    public async Task<JsonElement> ActivateAsync(params string[] services)
    {
        await StartAsync(services);
        await UploadAsync();
        if (services.Contains(FakeArca.InvoicingService))
        {
            Arca.PointsOfSale = [new FakeArcaPointOfSale(5, "CAE - Factura", "N", "NULL")];
            await ReadAsync(await Client.PutAsJsonAsync("/api/arca/invoicing/point-of-sale", new { pointOfSale = 5 }, Ct), HttpStatusCode.OK);
        }

        var connection = await GetConnectionAsync();
        Assert.Equal("Active", connection.GetProperty("status").GetString());
        return connection;
    }

    public async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(Ct);
            bodies.Add(text);
            Assert.True(response.StatusCode == expected, $"{response.StatusCode}: {text}");
            return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
        }
    }

    /// <summary>Desconecta, si quedó una conexión, para que el próximo test empiece sin ninguna.</summary>
    public async ValueTask DisposeAsync()
    {
        using var current = await Client.GetAsync("/api/arca/connection", Ct);
        var revision = (await current.ReadJsonAsync()).GetProperty("revision").GetInt64();
        if (revision > 0) (await Client.DeleteAsync($"/api/arca/connection?expectedRevision={revision}", Ct)).Dispose();
        Client.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
