using System.Net;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class UtcDateTimeTests(ApiFactory factory)
{
    [Fact]
    public async Task Date_with_offset_is_returned_in_utc_with_z()
    {
        using var client = factory.CreateClient();
        using var content = Json("""{ "occurredAtUtc": "2026-09-18T10:00:00-03:00", "expiresAtUtc": null }""");

        using var response = await client.SendAsync(HttpMethod.Post, "/test/dates", content);
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2026-09-18T13:00:00Z", body.GetProperty("occurredAtUtc").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("expiresAtUtc").ValueKind);
    }

    [Fact]
    public async Task Date_without_offset_is_rejected_with_400()
    {
        using var client = factory.CreateClient();
        using var content = Json("""{ "occurredAtUtc": "2026-09-18T10:00:00" }""");

        using var response = await client.SendAsync(HttpMethod.Post, "/test/dates", content);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
