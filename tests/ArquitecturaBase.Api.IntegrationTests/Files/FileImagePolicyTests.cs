using ArquitecturaBase.Api.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
namespace ArquitecturaBase.Api.IntegrationTests.Files;
public sealed class FileImagePolicyTests
{
    /// <summary>Las copias privadas en blob se ven en img e iframe, y el único marco de otro sitio es la vista oficial de
    /// WhatsApp, la página de Meta (VO1-A, 2026-11-06); los scripts siguen siendo sólo los propios.</summary>
    [Fact]
    public async Task Private_blob_images_pdf_frames_and_the_official_whatsapp_preview_are_allowed_without_enabling_blob_scripts()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        await using var app = builder.Build(); app.UseSecurityHeaders();
        app.Run(context => context.Response.WriteAsync("private image host"));
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient(); using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("img-src 'self' data: blob: https:", policy, StringComparison.Ordinal);
        Assert.Contains("script-src 'self';", policy, StringComparison.Ordinal);
        Assert.Contains("frame-src 'self' blob: https://business.facebook.com;", policy, StringComparison.Ordinal);
    }
}
