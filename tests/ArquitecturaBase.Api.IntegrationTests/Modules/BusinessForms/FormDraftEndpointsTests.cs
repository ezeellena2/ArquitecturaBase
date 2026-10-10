using System.Text.Json.Nodes;
using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed partial class FormDraftEndpointsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Complete_creation_replays_the_same_snapshot_and_identity_map_across_hosts()
    {
        using var signedIn = factory.CreateClient();
        await signedIn.LoginAsync(factory, ApiFactory.AdminEmail);
        var actor = await factory.ExecuteDbContextAsync(db => db.Users.Where(x => x.Email == ApiFactory.AdminEmail).Select(x => x.Id).SingleAsync(Ct));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.ToString());
        var requestId = Guid.NewGuid();
        var payload = new
        {
            requestId, name = new { es = "Creación completa", en = "Complete creation" }, prefix = "T" + Guid.NewGuid().ToString("N")[..10],
            draft = new { format = 1, culture = "es", submitLabel = new { es = "Enviar" },
                fields = new[] { new { localId = "a", type = "longText", label = new { es = "Consulta" }, required = false } } },
        };
        using var created = await client.PostAsJsonAsync("/api/forms/definitions", payload, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var original = await created.ReadJsonAsync();
        Assert.Equal(600, original.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("maxLength").GetInt32());
        Assert.Equal(1, original.GetProperty("definition").GetProperty("revision").GetInt64());
        await using var host = factory.WithWebHostBuilder(_ => { });
        using var second = host.CreateClient();
        second.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.ToString());
        using var replay = await second.PostAsJsonAsync("/api/forms/definitions", payload, Ct);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(original.GetRawText(), (await replay.ReadJsonAsync()).GetRawText());
        using var recovered = await second.GetAsync("/api/forms/draft-changes/" + requestId, Ct);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original.GetRawText()), JsonNode.Parse((await recovered.ReadJsonAsync()).GetProperty("result").GetRawText())));
    }
}
