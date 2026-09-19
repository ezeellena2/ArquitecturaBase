using System.Text.Json;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal static class HttpExtensions
{
    public static async Task<HttpResponseMessage> SendAsync(
        this HttpClient client,
        HttpMethod method,
        string url,
        HttpContent? content = null,
        string? language = null,
        string? userId = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative)) { Content = content };

        if (language is not null)
        {
            request.Headers.AcceptLanguage.ParseAdd(language);
        }

        if (userId is not null)
        {
            request.Headers.Add(TestAuthHandler.UserIdHeader, userId);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }
}
