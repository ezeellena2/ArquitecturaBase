using ArquitecturaBase.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
namespace ArquitecturaBase.Api.IntegrationTests.Files;
public sealed class FileImagePolicyTests
{
    [Fact]
    public async Task Private_blob_images_and_pdf_frames_are_allowed_without_enabling_blob_scripts()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        await using var app = builder.Build(); app.UseSecurityHeaders();
        app.Run(context => context.Response.WriteAsync("private image host"));
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient(); using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("img-src 'self' data: blob: https:", policy, StringComparison.Ordinal);
        Assert.Contains("script-src 'self';", policy, StringComparison.Ordinal);
        Assert.Contains("frame-src 'self' blob:;", policy, StringComparison.Ordinal);
    }
}
