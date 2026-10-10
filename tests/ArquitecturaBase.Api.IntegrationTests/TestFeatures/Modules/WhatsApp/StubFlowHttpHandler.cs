namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class StubFlowHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> Bodies { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        return response(request);
    }
}
