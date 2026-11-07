using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.Hosting;

/// <summary>
/// La Api comprime el SPA y el JSON aunque la petición llegue por HTTPS, que es como llega siempre detrás del
/// proxy: el cliente del arnés usa https://localhost.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ResponseCompressionTests(ApiFactory factory)
{
    [Theory]
    [InlineData("/usuarios", "br")]
    [InlineData("/assets/main.js", "br")]
    [InlineData("/.well-known/openid-configuration", "br")]
    [InlineData("/assets/main.js", "gzip")]
    public async Task Text_responses_are_compressed_over_https(string url, string encoding)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Relative));
        request.Headers.AcceptEncoding.ParseAdd(encoding);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(encoding, Assert.Single(response.Content.Headers.ContentEncoding));
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
    }
}
