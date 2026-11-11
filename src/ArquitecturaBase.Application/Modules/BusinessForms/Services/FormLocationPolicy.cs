using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The answer to a location question (UB1-A) as the form sends it: <c>{"latitude":…,"longitude":…,"accuracyMeters":…}</c>, numbers
/// only and nothing else. Latitude goes from -90 to 90 and longitude from -180 to 180, both rounded to six decimals like the
/// coordinates of the business; the optional accuracy goes from 0 to 100,000 meters, rounded to whole meters. It is what the device
/// reported: nothing checks that the point is real.
/// </summary>
internal static class FormLocationPolicy
{
    public const int MaxAccuracyMeters = 100_000;

    /// <summary>The checked point; null when the text is not one.</summary>
    public static StructuredRecordLocationValue? Parse(string raw)
    {
        try
        {
            using var json = JsonDocument.Parse(raw);
            if (json.RootElement.ValueKind != JsonValueKind.Object) return null;
            decimal? latitude = null, longitude = null, accuracy = null;
            foreach (var property in json.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDecimal(out var number)) return null;
                switch (property.Name)
                {
                    case "latitude" when latitude is null: latitude = number; break;
                    case "longitude" when longitude is null: longitude = number; break;
                    case "accuracyMeters" when accuracy is null: accuracy = number; break;
                    default: return null;
                }
            }
            if (latitude is not (>= -90m and <= 90m) || longitude is not (>= -180m and <= 180m)) return null;
            int? meters = null;
            if (accuracy is { } value)
            {
                var rounded = Math.Round(value, 0, MidpointRounding.AwayFromZero);
                if (rounded is < 0m or > MaxAccuracyMeters) return null;
                meters = (int)rounded;
            }
            return new(Math.Round(latitude.Value, 6, MidpointRounding.AwayFromZero), Math.Round(longitude.Value, 6, MidpointRounding.AwayFromZero), meters);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The point as the form sends it, to fill the form again (a correction keeps the point it had).</summary>
    public static string Canonical(StructuredRecordLocationValue location) => JsonSerializer.Serialize(location, JsonSerializerOptions.Web);
}
