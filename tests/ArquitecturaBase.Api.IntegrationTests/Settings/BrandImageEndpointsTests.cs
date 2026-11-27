using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;

namespace ArquitecturaBase.Api.IntegrationTests.Settings;

// ME2: the logo and the icon are uploaded to the installation's store, saved with the revision the screen read and served
// without a session, cached for a year when the address carries their version.
[Collection(ApiTestGroup.Name)]
public sealed class BrandImageEndpointsTests(ApiFactory factory)
{
    [Fact]
    public async Task A_logo_is_uploaded_seen_publicly_cached_by_version_and_removed()
    {
        var files = new MemoryBrandFiles();
        await using var host = WithFiles(files);
        using var client = host.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var revision = await RevisionAsync(client, tokens.AccessToken);

        using var uploaded = await UploadAsync(client, tokens.AccessToken, "logo", revision, Png(400, 112));
        var body = await uploaded.ReadJsonAsync();
        var version = body.GetProperty("version").GetString()!;
        try
        {
            Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
            Assert.Equal(revision + 1, body.GetProperty("revision").GetInt64());
            Assert.Single(files.Objects.Keys, key => key == $"branding/logo/{version}.png");
            using var settings = await client.GetWithTokenAsync("/api/settings", tokens.AccessToken);
            Assert.Equal(version, (await settings.ReadJsonAsync()).GetProperty("branding").GetProperty("logoVersion").GetString());

            using var anonymous = host.CreateClient();
            using var presentation = await anonymous.GetAsync("/api/settings/presentation", Ct);
            var brand = (await presentation.ReadJsonAsync()).GetProperty("brand");
            Assert.Equal(version, brand.GetProperty("logoVersion").GetString());
            Assert.Equal(JsonValueKind.Null, brand.GetProperty("iconVersion").ValueKind);

            using var cached = await anonymous.GetAsync($"/api/settings/brand/logo?v={version}", Ct);
            Assert.Equal(HttpStatusCode.OK, cached.StatusCode);
            Assert.Equal("image/png", cached.Content.Headers.ContentType?.MediaType);
            Assert.True(cached.Headers.CacheControl is { Public: true, MaxAge.TotalDays: 365 });
            Assert.Equal($"\"{version}\"", cached.Headers.ETag?.Tag);
            using var unversioned = await anonymous.GetAsync("/api/settings/brand/logo", Ct);
            Assert.True(unversioned.Headers.CacheControl?.NoCache);
            using var revalidated = new HttpRequestMessage(HttpMethod.Get, "/api/settings/brand/logo");
            revalidated.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
            using var notModified = await anonymous.SendAsync(revalidated, Ct);
            Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        }
        finally
        {
            using var removed = await RemoveAsync(client, tokens.AccessToken, "logo", await RevisionAsync(client, tokens.AccessToken));
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            Assert.Equal(JsonValueKind.Null, (await removed.ReadJsonAsync()).GetProperty("version").ValueKind);
        }

        using var gone = await client.GetAsync("/api/settings/brand/logo", Ct);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task A_stale_revision_is_a_conflict()
    {
        await using var host = WithFiles(new MemoryBrandFiles());
        using var client = host.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var revision = await RevisionAsync(client, tokens.AccessToken);

        using var response = await UploadAsync(client, tokens.AccessToken, "icon", revision - 1, Png(192, 192));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Settings.System.RevisionConflict", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("logo", 112, 400, "Settings.System.LogoInvalid", "El logo tiene que ser PNG o JPG de hasta 1 MB, igual de ancho o más ancho que alto.")]
    [InlineData("icon", 192, 120, "Settings.System.IconInvalid", "El ícono tiene que ser PNG cuadrado, de 64 a 1024 píxeles y hasta 1 MB.")]
    public async Task An_image_with_the_wrong_shape_is_refused_on_the_file_field(string kind, int width, int height, string code, string message)
    {
        var files = new MemoryBrandFiles();
        await using var host = WithFiles(files);
        using var client = host.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await UploadAsync(client, tokens.AccessToken, kind, await RevisionAsync(client, tokens.AccessToken), Png(width, height), "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(message, problem.GetProperty("errors").GetProperty("file")[0].GetString());
        Assert.Empty(files.Objects);
    }

    [Fact]
    public async Task Bytes_that_are_not_an_image_are_refused_whatever_their_name_says()
    {
        await using var host = WithFiles(new MemoryBrandFiles());
        using var client = host.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await UploadAsync(client, tokens.AccessToken, "logo", await RevisionAsync(client, tokens.AccessToken),
            "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Settings.System.LogoInvalid", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Uploading_and_removing_need_a_session_and_the_settings_permission()
    {
        await using var host = WithFiles(new MemoryBrandFiles());
        using var client = host.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("nobrand"));

        using var anonymous = await client.PutAsync("/api/settings/brand/logo", Form(1, Png(400, 112)), Ct);
        using var forbidden = await UploadAsync(client, tokens.AccessToken, "logo", 1, Png(400, 112));
        using var forbiddenRemove = await RemoveAsync(client, tokens.AccessToken, "icon", 1);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRemove.StatusCode);
    }

    private WebApplicationFactory<Program> WithFiles(MemoryBrandFiles files) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(files);
        }));

    private static async Task<long> RevisionAsync(HttpClient client, string accessToken)
    {
        using var response = await client.GetWithTokenAsync("/api/settings", accessToken);
        return (await response.ReadJsonAsync()).GetProperty("revision").GetInt64();
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string accessToken, string kind, long revision, byte[] content,
        string? language = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/settings/brand/{kind}") { Content = Form(revision, content) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (language is not null) request.Headers.AcceptLanguage.ParseAdd(language);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> RemoveAsync(HttpClient client, string accessToken, string kind, long revision)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/settings/brand/{kind}?expectedRevision={revision}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request, Ct);
    }

    private static MultipartFormDataContent Form(long revision, byte[] content)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent
        {
            { new StringContent(revision.ToString(System.Globalization.CultureInfo.InvariantCulture)), "expectedRevision" },
            { file, "file", "logo.png" },
        };
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(0xFF, 0xC1, 0x0E));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
