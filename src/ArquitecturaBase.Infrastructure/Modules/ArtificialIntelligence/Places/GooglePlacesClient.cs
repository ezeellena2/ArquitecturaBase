using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Places;
internal sealed class GooglePlacesClient(HttpClient http, IOptions<GooglePlacesOptions> options) : IBusinessPlacesClient, IDisposable
{
    private const int MaxResponseBytes = 32768;
    private readonly SemaphoreSlim slots = new(4, 4);
    public async Task<Result<BusinessPlacesResponse>> SearchAsync(string input, string languageCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey)) return BusinessPlacesErrors.NotConfigured;
        if (!await slots.WaitAsync(0, cancellationToken)) return BusinessPlacesErrors.Busy;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:autocomplete");
            request.Headers.Add("X-Goog-Api-Key", options.Value.ApiKey);
            request.Headers.Add("X-Goog-FieldMask", "suggestions.placePrediction.placeId,suggestions.placePrediction.text.text");
            var body = new Dictionary<string, object> { ["input"] = input, ["languageCode"] = languageCode };
            if (options.Value.RegionCode is { } region) body["regionCode"] = region.ToLowerInvariant();
            request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest) return BusinessPlacesErrors.ConfigurationInvalid;
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return BusinessPlacesErrors.RateLimited;
            if (!response.IsSuccessStatusCode) return BusinessPlacesErrors.Unavailable;
            if (response.Content.Headers.ContentLength > MaxResponseBytes) return BusinessPlacesErrors.ResponseInvalid;
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (bytes.Length + read > MaxResponseBytes) return BusinessPlacesErrors.ResponseInvalid;
                bytes.Write(buffer, 0, read);
            }
            using var json = JsonDocument.Parse(bytes.GetBuffer().AsMemory(0, checked((int)bytes.Length)), new JsonDocumentOptions { MaxDepth = 12 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return BusinessPlacesErrors.ResponseInvalid;
            // Protobuf JSON omits empty repeated fields.
            if (!root.TryGetProperty("suggestions", out var suggestions)) return new BusinessPlacesResponse([]);
            if (suggestions.ValueKind != JsonValueKind.Array || suggestions.GetArrayLength() > 5) return BusinessPlacesErrors.ResponseInvalid;
            var places = new List<BusinessPlaceResponse>();
            foreach (var suggestion in suggestions.EnumerateArray())
            {
                if (suggestion.ValueKind != JsonValueKind.Object || !suggestion.TryGetProperty("placePrediction", out var prediction)
                    || prediction.ValueKind != JsonValueKind.Object || !prediction.TryGetProperty("placeId", out var id)
                    || id.ValueKind != JsonValueKind.String || id.GetString() is not { } placeId || !BusinessLocation.IsValidPlaceId(placeId)
                    || !prediction.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.Object
                    || !text.TryGetProperty("text", out var labelValue) || labelValue.ValueKind != JsonValueKind.String
                    || labelValue.GetString() is not { Length: > 0 and <= 1000 } label || string.IsNullOrWhiteSpace(label)
                    || label.Any(char.IsControl)) return BusinessPlacesErrors.ResponseInvalid;
                if (places.All(place => place.PlaceId != placeId)) places.Add(new(placeId, label));
            }
            return new BusinessPlacesResponse(places.AsReadOnly());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return BusinessPlacesErrors.Unavailable; }
        catch (HttpRequestException) { return BusinessPlacesErrors.Unavailable; }
        catch (IOException) { return BusinessPlacesErrors.Unavailable; }
        catch (JsonException) { return BusinessPlacesErrors.ResponseInvalid; }
        finally { slots.Release(); }
    }
    public void Dispose() { slots.Dispose(); http.Dispose(); }
}
