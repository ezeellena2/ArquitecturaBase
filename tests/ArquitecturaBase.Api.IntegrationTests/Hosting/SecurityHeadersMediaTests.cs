using ArquitecturaBase.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Api.IntegrationTests.Hosting;
public sealed class SecurityHeadersMediaTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Private_audio_blob_playback_is_allowed_without_widening_scripts_or_connections(string environment)
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName=environment,Args=[]});
        builder.Logging.ClearProviders();builder.WebHost.UseTestServer();await using var app=builder.Build();
        app.UseSecurityHeaders();app.MapGet("/",()=>Results.Text("Synthetic hosting fixture"));
        await app.StartAsync(TestContext.Current.CancellationToken);using var client=app.GetTestClient();
        using var response=await client.GetAsync("/",TestContext.Current.CancellationToken);
        var policy=Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        var directives=policy.Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("media-src 'self' blob:",directives);Assert.Contains("script-src 'self'",directives);Assert.Contains("connect-src 'self'",directives);
        Assert.DoesNotContain(directives,item=>item.StartsWith("media-src",StringComparison.Ordinal) && (item.Contains("https:",StringComparison.Ordinal) || item.Contains('*',StringComparison.Ordinal)));
    }
}
