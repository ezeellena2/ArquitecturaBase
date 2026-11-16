using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.RateLimiting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Hosting;

/// <summary>
/// Los límites de todos los pedidos (N6): una ventana por IP para las rutas anónimas sin política propia, un tope de
/// pedidos a la vez que no frena a los probes, y el tope del cuerpo en Kestrel, que una subida con su propio tope supera.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class GlobalRequestLimitsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anonymous_routes_share_a_window_per_address_and_answer_429_when_it_runs_out()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("RequestLimits:AnonymousPermitLimit", "2"));
        using var client = api.CreateClient();

        using var methods = await client.GetAsync("/account/login-methods", Ct);
        using var health = await client.GetAsync("/health", Ct);
        using var presentation = await client.GetAsync("/api/settings/presentation", Ct);
        using var signedIn = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
        signedIn.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture));
        using var me = await client.SendAsync(signedIn, Ct);

        Assert.Equal(HttpStatusCode.OK, methods.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, presentation.StatusCode);
        Assert.True(presentation.Headers.RetryAfter?.Delta >= TimeSpan.FromSeconds(1));
        var problem = await presentation.ReadJsonAsync();
        Assert.True(problem.GetProperty("retryAfter").GetInt32() >= 1);
        // Una ruta que pide sesión no cuenta en la ventana de lo anónimo.
        Assert.NotEqual(HttpStatusCode.TooManyRequests, me.StatusCode);
    }

    [Fact]
    public async Task Requests_in_flight_beyond_the_limit_are_rejected_but_the_probes_are_not()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("RequestLimits:ConcurrentRequests", "1")
            .UseSetting("RequestLimits:QueuedRequests", "0"));
        var limiter = api.Services.GetRequiredService<GlobalRequestLimiter>().Limiter;

        using var first = limiter.AttemptAcquire(Request("/api/users"));
        using var second = limiter.AttemptAcquire(Request("/assets/main.js"));
        using var health = limiter.AttemptAcquire(Request("/health"));
        using var alive = limiter.AttemptAcquire(Request("/alive"));
        first.Dispose();
        using var afterwards = limiter.AttemptAcquire(Request("/api/users"));

        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
        Assert.True(health.IsAcquired);
        Assert.True(alive.IsAcquired);
        Assert.True(afterwards.IsAcquired);
    }

    [Fact]
    public void Kestrel_accepts_bodies_of_up_to_one_mebibyte_by_default()
    {
        var kestrel = factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        Assert.Equal(1024 * 1024, kestrel.Limits.MaxRequestBodySize);
    }

    /// <summary>Con Kestrel de verdad: el servidor de prueba no aplica topes de cuerpo.</summary>
    [Fact]
    public async Task A_body_over_the_global_limit_gets_413_and_an_upload_with_its_own_limit_still_works()
    {
        const int globalLimit = 64 * 1024;
        const int body = 3 * globalLimit;
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("RequestLimits:MaxRequestBodyBytes", globalLimit.ToString(CultureInfo.InvariantCulture)));
        api.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        api.StartServer();
        var address = api.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };

        using var json = new StringContent(
            "{\"occurredAtUtc\":\"" + new string('x', body) + "\"}", Encoding.UTF8, "application/json");
        using var tooLarge = await client.PostAsync(new Uri("/test/dates", UriKind.Relative), json, Ct);
        using var file = new ByteArrayContent(new byte[body]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var upload = await client.PostAsync(new Uri("/test/upload", UriKind.Relative), file, Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal(body, (await upload.ReadJsonAsync()).GetProperty("length").GetInt32());
        Assert.True(body < TestController.UploadLimitBytes);
    }

    private static DefaultHttpContext Request(string path) => new() { Request = { Path = path } };
}
