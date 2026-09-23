using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// La Graph API de mentira: guarda cada pedido y responde lo que diga el test, según el número de llamada (desde 1).
/// Los tests nunca llaman a Meta ni usan un token real.
/// </summary>
internal sealed class FakeMetaHandler(Func<int, HttpResponseMessage>? respond = null) : HttpMessageHandler
{
    public const string WaMessageId = "wamid.HBgLNTQ5MzQxMTIzNDU2NxUCABEYEjAwMDAwMDAwMDAwMDAwMDAwMAA=";

    private readonly ConcurrentQueue<RecordedRequest> _requests = new();
    private readonly Func<int, HttpResponseMessage> _respond = respond ?? (_ => Success());
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public IReadOnlyList<RecordedRequest> Requests => [.. _requests];

    /// <summary>La respuesta de un envío aceptado, como la documenta Meta (con el número en <c>contacts</c>).</summary>
    public static HttpResponseMessage Success(string waMessageId = WaMessageId) => Json(
        HttpStatusCode.OK,
        $$"""{"messaging_product":"whatsapp","contacts":[{"input":"+5493411234567","wa_id":"5493411234567"}],"messages":[{"id":"{{waMessageId}}"}]}""");

    /// <summary>Un error de la Graph API con su código, con el formato de la documentación de Meta.</summary>
    public static HttpResponseMessage Error(HttpStatusCode status, int code) => Json(
        status,
        string.Create(
            CultureInfo.InvariantCulture,
            $$$"""{"error":{"message":"(#{{{code}}}) Error","type":"OAuthException","code":{{{code}}},"error_data":{"messaging_product":"whatsapp","details":"Details"},"fbtrace_id":"AbCdEfGh123"}}"""));

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));

        return _respond(Interlocked.Increment(ref _calls));
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body);
