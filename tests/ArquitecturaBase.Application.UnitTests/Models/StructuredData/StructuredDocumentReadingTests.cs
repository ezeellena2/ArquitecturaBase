using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.UnitTests.Models.StructuredData;

/// <summary>
/// N27 and H25: a reading keeps its reason by name, as before; a reason a newer version writes reads as Other here, and a field
/// that restricts (how readable it is) does not.
/// </summary>
public sealed class StructuredDocumentReadingTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new StructuredDocumentProblemJsonConverter(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    [Fact]
    public void A_reading_keeps_its_reason_by_name_as_before()
    {
        var json = JsonSerializer.Serialize(new StructuredDocumentReading(StructuredDocumentReadability.Partial, StructuredDocumentProblem.Blurry,
            "es", "Texto", false), Options);

        Assert.Equal("""{"readability":"partial","problem":"blurry","language":"es","text":"Texto","truncated":false}""", json);
        Assert.Equal(StructuredDocumentProblem.Blurry, JsonSerializer.Deserialize<StructuredDocumentReading>(json, Options)!.Problem);
    }

    [Fact]
    public void A_reason_this_version_does_not_know_reads_as_other()
    {
        var reading = JsonSerializer.Deserialize<StructuredDocumentReading>(
            """{"readability":"partial","problem":"glare","text":"Texto","truncated":false}""", Options)!;

        Assert.Equal((StructuredDocumentReadability.Partial, (StructuredDocumentProblem?)StructuredDocumentProblem.Other), (reading.Readability, reading.Problem));
        Assert.Null(JsonSerializer.Deserialize<StructuredDocumentReading>("""{"readability":"readable","text":"Texto","truncated":false}""", Options)!.Problem);
    }

    [Fact]
    public void Options_that_keep_enums_as_numbers_keep_the_reason_as_a_number()
    {
        var reading = new StructuredDocumentReading(StructuredDocumentReadability.Partial, StructuredDocumentProblem.Blurry, null, "Texto", false);

        var json = JsonSerializer.Serialize(reading, JsonSerializerOptions.Web);

        Assert.Equal("""{"readability":1,"problem":0,"text":"Texto","truncated":false}""", json);
        Assert.Equal(StructuredDocumentProblem.Blurry, JsonSerializer.Deserialize<StructuredDocumentReading>(json, JsonSerializerOptions.Web)!.Problem);
    }

    [Fact]
    public void A_readability_this_version_does_not_know_does_not_read()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StructuredDocumentReading>(
            """{"readability":"halfway","text":"Texto","truncated":false}""", Options));
    }
}
