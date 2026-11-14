using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Timeout;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>
/// El HttpClient tipado de la Graph API de WhatsApp (sección 9 del spec): un POST a
/// <c>https://graph.facebook.com/{versión}/{phone_number_id}/messages</c> con el token como Bearer.
/// Traduce la respuesta a un <see cref="WhatsAppSendResult"/>: los errores de Meta, la red y los timeouts son un
/// resultado, no una excepción. No reintenta ni registra nada: de eso se ocupa la cola, que sabe a quién le escribía.
/// </summary>
internal sealed class WhatsAppCloudClient(HttpClient httpClient, IOptions<WhatsAppOptions> options) : IWhatsAppCloudClient
{
    public static readonly Uri GraphApiAddress = new("https://graph.facebook.com/");

    public async Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var settings = options.Value;
        var path = $"{settings.GraphApiVersion}/{Uri.EscapeDataString(settings.PhoneNumberId ?? string.Empty)}/messages";

        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        request.Content = new StringContent(
            WhatsAppMessagePayload.Build(message, settings).ToJsonString(), Encoding.UTF8, "application/json");

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await ReadJsonAsync(response, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                // Sin el id, Meta pudo haberlo mandado igual: no se reintenta, para no duplicar el mensaje.
                return MessageIdOf(body) is { } waMessageId
                    ? WhatsAppSendResult.Sent(waMessageId)
                    : WhatsAppSendResult.Uncertain();
            }

            var metaErrorCode = ErrorCodeOf(body);

            // H7: un 5xx sin código de Meta (un proxy, una caída a mitad de camino) no dice si lo procesó: no se sabe.
            if (metaErrorCode is null && (int)response.StatusCode >= 500) return WhatsAppSendResult.Uncertain();

            return WhatsAppSendResult.Failed(FailureFor(response.StatusCode, metaErrorCode), metaErrorCode);
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError is HttpRequestError.NameResolutionError
            or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError)
        {
            // H7: el DNS, la conexión o el TLS no llegaron a Meta: no salió, y se reintenta.
            return WhatsAppSendResult.Failed(WhatsAppSendFailure.Transient);
        }
        catch (HttpRequestException)
        {
            // Una respuesta cortada o inválida: Meta pudo haberlo recibido.
            return WhatsAppSendResult.Uncertain();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // El timeout del HttpClient: Meta pudo haberlo recibido. La cancelación de la app, en cambio, se propaga.
            return WhatsAppSendResult.Uncertain();
        }
        catch (TimeoutRejectedException)
        {
            // El timeout de la resiliencia estándar: como el del HttpClient, no se sabe.
            return WhatsAppSendResult.Uncertain();
        }
        catch (ExecutionRejectedException)
        {
            // El circuit breaker abierto o el límite de la resiliencia cortaron antes de mandar: no salió.
            return WhatsAppSendResult.Failed(WhatsAppSendFailure.Transient);
        }
    }

    /// <summary>
    /// Manda el código de Meta, que es lo que recomienda su documentación, y gana sobre el status: el 3, por ejemplo,
    /// llega con un 500 y no es pasajero. Los de autorización salen de la tabla "Authorization errors" de Meta. H7: de un
    /// 5xx sólo se reintentan los que Meta manda reintentar (131000, 131016, 2 y 133004); otro código dice qué pasó y no
    /// se insiste, y un 5xx sin código no llega acá, porque no se sabe si salió. Sin un código conocido, el status: un 401
    /// es el token, un 403 un permiso y un 429 un límite, que se reintenta.
    /// </summary>
    private static WhatsAppSendFailure FailureFor(HttpStatusCode status, int? metaErrorCode) => metaErrorCode switch
    {
        131030 => WhatsAppSendFailure.RecipientNotAllowed,
        131047 => WhatsAppSendFailure.OutsideCustomerServiceWindow,
        131026 => WhatsAppSendFailure.Undeliverable,
        131056 => WhatsAppSendFailure.PairRateLimited,
        130429 => WhatsAppSendFailure.RateLimited,

        // H7: «Something went wrong», «Service unavailable», «Temporary due to downtime» y «Server is temporarily
        // unavailable», con un 5xx: Meta pide reintentarlos.
        131000 or 131016 or 2 or 133004 when (int)status >= 500 => WhatsAppSendFailure.Transient,

        // 0: AuthException. 190: el token venció o lo revocaron.
        0 or 190 => WhatsAppSendFailure.InvalidToken,

        // 3: capacidad o permisos. 10: permiso denegado. De 200 a 299: un permiso de la API.
        3 or 10 or (>= 200 and <= 299) => WhatsAppSendFailure.MissingPermission,

        _ when status == HttpStatusCode.Unauthorized => WhatsAppSendFailure.InvalidToken,
        _ when status == HttpStatusCode.Forbidden => WhatsAppSendFailure.MissingPermission,
        _ when status == HttpStatusCode.TooManyRequests => WhatsAppSendFailure.RateLimited,
        _ => WhatsAppSendFailure.Other,
    };

    // Un cuerpo que no es JSON (una página de error de un proxy, por ejemplo) no es una excepción: decide el status.
    private static async Task<JsonElement?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // { "messages": [ { "id": "wamid...." } ] }
    private static string? MessageIdOf(JsonElement? body) =>
        body is { ValueKind: JsonValueKind.Object } root
        && root.TryGetProperty("messages", out var messages)
        && messages.ValueKind == JsonValueKind.Array
        && messages.GetArrayLength() > 0
        && messages[0].ValueKind == JsonValueKind.Object
        && messages[0].TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(id.GetString())
            ? id.GetString()
            : null;

    // { "error": { "code": 131047, ... } }. El mensaje y los detalles de Meta no se leen: pueden repetir lo que se mandó.
    private static int? ErrorCodeOf(JsonElement? body) =>
        body is { ValueKind: JsonValueKind.Object } root
        && root.TryGetProperty("error", out var error)
        && error.ValueKind == JsonValueKind.Object
        && error.TryGetProperty("code", out var code)
        && code.ValueKind == JsonValueKind.Number
        && code.TryGetInt32(out var value)
            ? value
            : null;
}
