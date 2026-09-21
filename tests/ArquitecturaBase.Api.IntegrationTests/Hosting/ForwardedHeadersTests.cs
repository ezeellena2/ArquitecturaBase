using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.Hosting;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Hosting;

[Collection(ApiTestGroup.Name)]
public sealed class ForwardedHeadersTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Trusted_proxy_sets_the_public_scheme_and_client_ip_but_not_the_host()
    {
        await using var api = CreateApi("10.0.0.4", ("ForwardedHeaders:TrustAll", "true"));
        using var client = CreateHttpClient(api);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/request-context");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "198.51.100.1, 203.0.113.10");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "evil.example");

        using var response = await client.SendAsync(request, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https", body.RootElement.GetProperty("scheme").GetString());
        Assert.Equal("203.0.113.10", body.RootElement.GetProperty("remoteIpAddress").GetString());
        Assert.Equal("localhost", body.RootElement.GetProperty("host").GetString());
    }

    [Fact]
    public async Task Headers_from_an_unknown_proxy_are_ignored()
    {
        await using var api = CreateApi("198.51.100.200");
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/request-context");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");

        using var response = await client.SendAsync(request, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https", body.RootElement.GetProperty("scheme").GetString());
        Assert.Equal("198.51.100.200", body.RootElement.GetProperty("remoteIpAddress").GetString());
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0", "10.0.0.4")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8")]
    public async Task Explicit_allowlist_trusts_a_matching_proxy(string key, string value)
    {
        await using var api = CreateApi("10.0.0.4", (key, value));
        using var client = CreateHttpClient(api);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/request-context");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request, Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(Ct));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https", body.RootElement.GetProperty("scheme").GetString());
        Assert.Equal("203.0.113.10", body.RootElement.GetProperty("remoteIpAddress").GetString());
    }

    [Fact]
    public void Configuration_uses_one_hop_and_only_for_and_proto()
    {
        using var services = CreateServices(
            ("ForwardedHeaders:KnownProxies:0", "203.0.113.7"),
            ("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8"));

        var options = services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Contains(IPAddress.Parse("203.0.113.7"), options.KnownProxies);
        Assert.Contains(System.Net.IPNetwork.Parse("10.0.0.0/8"), options.KnownIPNetworks);
    }

    [Fact]
    public void Trust_all_clears_both_trust_lists()
    {
        using var services = CreateServices(("ForwardedHeaders:TrustAll", "true"));

        var options = services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0", "not-an-ip")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "not-a-network")]
    public void Invalid_trust_entries_fail_fast(string key, string value)
    {
        using var services = CreateServices((key, value));

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value);

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Trust_all_cannot_be_combined_with_explicit_entries()
    {
        using var services = CreateServices(
            ("ForwardedHeaders:TrustAll", "true"),
            ("ForwardedHeaders:KnownProxies:0", "203.0.113.7"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value);

        Assert.Contains("TrustAll", exception.Message, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateApi(
        string remoteIpAddress,
        params (string Key, string Value)[] settings) =>
        factory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(IPAddress.Parse(remoteIpAddress))));
        });

    private static HttpClient CreateHttpClient(WebApplicationFactory<Program> api) =>
        api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

    private static ServiceProvider CreateServices(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value =>
                new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddTrustedForwardedHeaders(configuration);

        return services.BuildServiceProvider();
    }

    private sealed class RemoteIpStartupFilter(IPAddress remoteIpAddress) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                context.Connection.RemoteIpAddress = remoteIpAddress;
                await following();
            });
            next(app);
        };
    }
}
