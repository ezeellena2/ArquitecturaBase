using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>
/// Consumo de WhatsApp (T7, PR14): one GET to <c>https://graph.facebook.com/{version}/{WabaId}</c> asking for the account's currency and its
/// <c>pricing_analytics</c> by day and pricing category for the installation's number, with the token as Bearer. It waits up to
/// <see cref="Deadline"/>, never retries (its HttpClient has no resilience) and saves nothing: an answer that is not a success, a timeout or a
/// body it cannot read is <c>WhatsApp.Usage.Unavailable</c>, and the screen offers to try again. The log carries neither the token nor the number.
/// </summary>
internal sealed partial class WhatsAppPricingAnalyticsClient(HttpClient httpClient, IOptions<WhatsAppOptions> options, WhatsAppHealth health,
    TimeProvider timeProvider, ILogger<WhatsAppPricingAnalyticsClient> logger) : IWhatsAppPricingAnalyticsClient
{
    /// <summary>How long the usage screen waits for Meta.</summary>
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    private const int MaximumBodyBytes = 1024 * 1024;

    public bool IsConfigured => options.Value.WabaId is { Length: > 0 and <= 64 } wabaId && wabaId.All(char.IsAsciiDigit)
        && !string.IsNullOrWhiteSpace(options.Value.AccessToken);

    public async Task<Result<WhatsAppPricingAnalytics>> ReadAsync(string phoneNumber, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(phoneNumber) || !phoneNumber.All(char.IsAsciiDigit))
            throw new ArgumentException("The phone number goes as its digits only.", nameof(phoneNumber));
        if (!IsConfigured) return WhatsAppUsageErrors.Unavailable;

        using var deadline = new CancellationTokenSource(Deadline, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, PathOf(phoneNumber, startUtc, endUtc));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.AccessToken);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            health.Observe(response.StatusCode, WhatsAppTokenUse.Management);
            if (!response.IsSuccessStatusCode)
            {
                Refused(logger, (int)response.StatusCode);
                return WhatsAppUsageErrors.Unavailable;
            }

            var analytics = await ReadBytesAsync(response.Content, linked.Token) is { } bytes ? Parse(bytes) : null;
            if (analytics is null)
            {
                Unreadable(logger);
                return WhatsAppUsageErrors.Unavailable;
            }

            return analytics;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TimedOut(logger, (int)Deadline.TotalSeconds);
            return WhatsAppUsageErrors.Unavailable;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            // Only the type: the message of a transport failure can carry the address, with the number in its query.
            Failed(logger, exception.GetType().Name);
            return WhatsAppUsageErrors.Unavailable;
        }
    }

    /// <summary>
    /// The account's currency and every data point, as <c>{ "currency": "USD", "pricing_analytics": { "data": [ { "data_points": [ {
    /// "pricing_category": "MARKETING", "volume": 120, "cost": 7.42 } ] } ] } }</c>; null if it does not have that shape. Meta leaves out
    /// <c>pricing_analytics</c>, its <c>data</c> or a <c>data_points</c> when the range has nothing, and that is no points.
    /// </summary>
    internal static WhatsAppPricingAnalytics? Parse(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "currency") is not { Length: > 0 } currency) return null;
            List<WhatsAppPricingPoint> points = [];
            if (!root.TryGetProperty("pricing_analytics", out var analytics)) return new(currency, points);
            if (analytics.ValueKind != JsonValueKind.Object) return null;
            if (!analytics.TryGetProperty("data", out var data)) return new(currency, points);
            if (data.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) return null;
                if (!item.TryGetProperty("data_points", out var dataPoints)) continue;
                if (dataPoints.ValueKind != JsonValueKind.Array) return null;
                foreach (var point in dataPoints.EnumerateArray())
                {
                    if (point.ValueKind != JsonValueKind.Object || Text(point, "pricing_category") is not { Length: > 0 } category
                        || !point.TryGetProperty("volume", out var volume) || volume.ValueKind != JsonValueKind.Number || !volume.TryGetInt64(out var delivered)
                        || !point.TryGetProperty("cost", out var cost) || cost.ValueKind != JsonValueKind.Number || !cost.TryGetDecimal(out var amount))
                        return null;
                    points.Add(new(category, delivered, amount));
                }
            }

            return new(currency, points);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The fields go encoded whole: their parentheses, brackets, quotes and commas are part of Meta's syntax, not of the address.
    private string PathOf(string phoneNumber, DateTime startUtc, DateTime endUtc)
    {
        var fields = string.Create(CultureInfo.InvariantCulture,
            $"currency,pricing_analytics.start({Unix(startUtc)}).end({Unix(endUtc)}).granularity(DAILY)"
            + $".phone_numbers([\"{phoneNumber}\"]).dimensions([\"PRICING_CATEGORY\"])");
        return $"{options.Value.GraphApiVersion}/{Uri.EscapeDataString(options.Value.WabaId!)}?fields={Uri.EscapeDataString(fields)}";
    }

    private static long Unix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static async Task<byte[]?> ReadBytesAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumBodyBytes) return null;
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0) return output.ToArray();
            if (output.Length + count > MaximumBodyBytes) return null;
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "WhatsApp pricing analytics answered {StatusCode}")]
    private static partial void Refused(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WhatsApp pricing analytics did not answer within {Seconds} seconds")]
    private static partial void TimedOut(ILogger logger, int seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WhatsApp pricing analytics answered a body that cannot be read")]
    private static partial void Unreadable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WhatsApp pricing analytics failed with {ExceptionType}")]
    private static partial void Failed(ILogger logger, string exceptionType);
}
