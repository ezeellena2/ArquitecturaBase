using System.Text.Json;
using ArquitecturaBase.Api.Json;

namespace ArquitecturaBase.Api.IntegrationTests.Json;

public sealed class UtcDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new UtcDateTimeConverter() } };

    private static readonly DateTime ExpectedUtc = new(2026, 9, 18, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Writes_utc_with_z_suffix()
    {
        var json = JsonSerializer.Serialize(new DateTime(2026, 9, 18, 17, 32, 0, DateTimeKind.Utc), Options);

        Assert.Equal("\"2026-09-18T17:32:00Z\"", json);
    }

    [Fact]
    public void Writes_fractional_seconds_only_when_present()
    {
        var json = JsonSerializer.Serialize(new DateTime(2026, 9, 18, 17, 32, 0, 120, DateTimeKind.Utc), Options);

        Assert.Equal("\"2026-09-18T17:32:00.12Z\"", json);
    }

    [Theory]
    [InlineData("2026-09-18T10:00:00-03:00")]
    [InlineData("2026-09-18T13:00:00Z")]
    [InlineData("2026-09-18T15:00:00+02:00")]
    public void Reads_values_with_offset_as_utc(string value)
    {
        var date = JsonSerializer.Deserialize<DateTime>($"\"{value}\"", Options);

        Assert.Equal(ExpectedUtc, date);
        Assert.Equal(DateTimeKind.Utc, date.Kind);
    }

    [Theory]
    [InlineData("2026-09-18T10:00:00")]
    [InlineData("2026-09-18")]
    [InlineData("hola")]
    [InlineData("")]
    public void Rejects_values_without_offset(string value)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>($"\"{value}\"", Options));
    }

    [Theory]
    [InlineData("0001-01-01T00:00:00+03:00")]
    [InlineData("9999-12-31T23:59:59.9999999-03:00")]
    public void Rejects_values_outside_the_utc_range(string value)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>($"\"{value}\"", Options));
    }

    [Fact]
    public void Nullable_dates_are_supported()
    {
        Assert.Null(JsonSerializer.Deserialize<DateTime?>("null", Options));
        Assert.Equal(ExpectedUtc, JsonSerializer.Deserialize<DateTime?>("\"2026-09-18T13:00:00Z\"", Options));
    }
}
