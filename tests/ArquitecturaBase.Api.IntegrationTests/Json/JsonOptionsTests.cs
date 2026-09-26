using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace ArquitecturaBase.Api.IntegrationTests.Json;

/// <summary>
/// Los controllers leen y escriben con las opciones de MVC; los ProblemDetails que se arman fuera de un controller y el
/// documento de OpenAPI, con las de <c>Microsoft.AspNetCore.Http</c>. Si divergen, una misma fecha o un mismo enum sale
/// distinto según quién arme la respuesta.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class JsonOptionsTests(ApiFactory factory)
{
    private static readonly JsonProbe Probe = new(
        new DateTime(2026, 9, 18, 17, 32, 0, 120, DateTimeKind.Utc),
        RegistrationMode.InviteOnly);

    [Fact]
    public void Controllers_and_the_rest_of_the_api_write_dates_and_enums_the_same_way()
    {
        var fromControllers = JsonSerializer.Serialize(Probe, MvcOptions());
        var fromTheRest = JsonSerializer.Serialize(Probe, HttpOptions());

        Assert.Equal("""{"atUtc":"2026-09-18T17:32:00.12Z","mode":"InviteOnly"}""", fromControllers);
        Assert.Equal(fromControllers, fromTheRest);
    }

    [Fact]
    public void Controllers_and_the_rest_of_the_api_read_dates_and_enums_the_same_way()
    {
        const string json = """{"atUtc":"2026-09-18T14:32:00.12-03:00","mode":"InviteOnly"}""";

        Assert.Equal(Probe, JsonSerializer.Deserialize<JsonProbe>(json, MvcOptions()));
        Assert.Equal(Probe, JsonSerializer.Deserialize<JsonProbe>(json, HttpOptions()));
    }

    [Fact]
    public void Controllers_and_the_rest_of_the_api_reject_dates_without_offset()
    {
        const string json = """{"atUtc":"2026-09-18T17:32:00","mode":"InviteOnly"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<JsonProbe>(json, MvcOptions()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<JsonProbe>(json, HttpOptions()));
    }

    private JsonSerializerOptions MvcOptions() =>
        factory.Services.GetRequiredService<IOptions<MvcJsonOptions>>().Value.JsonSerializerOptions;

    private JsonSerializerOptions HttpOptions() =>
        factory.Services.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;

    private sealed record JsonProbe(DateTime AtUtc, RegistrationMode Mode);
}
