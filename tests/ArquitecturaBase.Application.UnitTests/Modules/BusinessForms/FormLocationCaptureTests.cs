using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// UB1-A: a location arrives as {"latitude":…,"longitude":…,"accuracyMeters":…} and is kept as three decimal rows of its question,
// latitude at position 0, longitude at 1 and the accuracy, when it came, at 2. Six decimals, the accuracy in whole meters.
public sealed class FormLocationCaptureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BusinessFormCaptureFixture Fixture(bool required = true)
    {
        var id = Guid.CreateVersion7();
        return new([new CanonicalFormField(id, "field_" + id.ToString("N"), "location",
            new Dictionary<string, string> { ["es"] = "Dónde estás" }, null, required, null, null)]);
    }

    private static StructuredRecordCaptureRequest Request(BusinessFormCaptureFixture f, string raw) =>
        f.Request(new Dictionary<string, string> { [f.Key] = raw });

    private static Guid Field(BusinessFormCaptureFixture f) => Guid.ParseExact(f.Key[6..], "N");

    [Fact]
    public async Task A_location_is_kept_as_three_decimal_rows_of_its_question()
    {
        var f = Fixture();

        var result = await f.Sink.CaptureAsync(Request(f, """{"latitude":-34.603722,"longitude":-58.381592,"accuracyMeters":15}"""), Ct);

        Assert.Equal("Completed", result.State);
        var record = Assert.Single(f.Repository.Records);
        var values = f.Repository.Values.OrderBy(value => value.Position).ToArray();
        Assert.Equal([0, 1, 2], values.Select(value => value.Position));
        Assert.Equal([-34.603722m, -58.381592m, 15m], values.Select(value => value.DecimalValue!.Value));
        Assert.All(values, value =>
        {
            Assert.Equal("location", value.Type);
            Assert.Equal(Field(f), value.FieldId);
            Assert.Equal(record.Id, value.RecordId);
            Assert.Equal(0, value.Row);
            Assert.Null(value.TextValue);
            Assert.Null(value.ChoiceId);
        });
        using var json = JsonDocument.Parse(record.ValuesJson);
        var answer = Assert.Single(json.RootElement.EnumerateArray()).GetProperty("value");
        Assert.Equal(-34.603722m, answer.GetProperty("latitude").GetDecimal());
        Assert.Equal(-58.381592m, answer.GetProperty("longitude").GetDecimal());
        Assert.Equal(15, answer.GetProperty("accuracyMeters").GetInt32());
    }

    [Theory]
    [InlineData("-34.6037224", "-58.3815916", "14.4", "-34.603722", "-58.381592", 14)]
    [InlineData("-34.6037225", "58.3815915", "14.5", "-34.603723", "58.381592", 15)]
    [InlineData("90", "-180", "0", "90", "-180", 0)]
    [InlineData("-90", "180", "100000", "-90", "180", 100000)]
    public async Task Coordinates_keep_six_decimals_and_the_accuracy_whole_meters(string latitude, string longitude, string accuracy,
        string expectedLatitude, string expectedLongitude, int expectedAccuracy)
    {
        var f = Fixture();

        var result = await f.Sink.CaptureAsync(Request(f, $$"""{"latitude":{{latitude}},"longitude":{{longitude}},"accuracyMeters":{{accuracy}}}"""), Ct);

        Assert.Equal("Completed", result.State);
        var values = f.Repository.Values.OrderBy(value => value.Position).Select(value => value.DecimalValue!.Value).ToArray();
        Assert.Equal(decimal.Parse(expectedLatitude, CultureInfo.InvariantCulture), values[0]);
        Assert.Equal(decimal.Parse(expectedLongitude, CultureInfo.InvariantCulture), values[1]);
        Assert.Equal(expectedAccuracy, values[2]);
    }

    [Fact]
    public async Task The_accuracy_is_optional()
    {
        var f = Fixture();

        var result = await f.Sink.CaptureAsync(Request(f, """{"longitude":-58.381592,"latitude":-34.603722}"""), Ct);

        Assert.Equal("Completed", result.State);
        Assert.Equal([0, 1], f.Repository.Values.OrderBy(value => value.Position).Select(value => value.Position));
        using var json = JsonDocument.Parse(Assert.Single(f.Repository.Records).ValuesJson);
        var answer = Assert.Single(json.RootElement.EnumerateArray()).GetProperty("value");
        Assert.False(answer.TryGetProperty("accuracyMeters", out _));
    }

    [Theory]
    [InlineData("""{"latitude":90.5,"longitude":0}""")]
    [InlineData("""{"latitude":-91,"longitude":0}""")]
    [InlineData("""{"latitude":0,"longitude":180.000001}""")]
    [InlineData("""{"latitude":0,"longitude":-181}""")]
    [InlineData("""{"latitude":0,"longitude":0,"accuracyMeters":-1}""")]
    [InlineData("""{"latitude":0,"longitude":0,"accuracyMeters":100001}""")]
    [InlineData("""{"latitude":0}""")]
    [InlineData("""{"longitude":0}""")]
    [InlineData("""{"latitude":"-34.6","longitude":"-58.3"}""")]
    [InlineData("""{"latitude":null,"longitude":0}""")]
    [InlineData("""{"latitude":0,"longitude":0,"accuracyMeters":null}""")]
    [InlineData("""{"latitude":0,"longitude":0,"altitude":10}""")]
    [InlineData("""{"latitude":0,"latitude":1,"longitude":0}""")]
    [InlineData("""{"latitude":1e400,"longitude":0}""")]
    [InlineData("""[-34.6,-58.3]""")]
    [InlineData("-34.6, -58.3")]
    [InlineData("null")]
    [InlineData("{")]
    public async Task A_malformed_location_keeps_only_the_rejected_receipt(string raw)
    {
        var f = Fixture();

        Rejected(f, await f.Sink.CaptureAsync(Request(f, raw), Ct));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_required_location_cannot_be_left_empty(bool supplied)
    {
        var f = Fixture();

        Rejected(f, await f.Sink.CaptureAsync(supplied ? Request(f, "") : f.Request(new Dictionary<string, string>()), Ct));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_optional_location_may_be_left_empty(bool supplied)
    {
        var f = Fixture(required: false);

        var result = await f.Sink.CaptureAsync(supplied ? Request(f, "") : f.Request(new Dictionary<string, string>()), Ct);

        Assert.Equal("Completed", result.State);
        Assert.Empty(f.Repository.Values);
        using var json = JsonDocument.Parse(Assert.Single(f.Repository.Records).ValuesJson);
        Assert.Empty(json.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task A_retry_returns_the_same_response_and_a_changed_point_is_a_conflict()
    {
        var f = Fixture();
        var request = Request(f, """{"latitude":-34.603722,"longitude":-58.381592,"accuracyMeters":15}""");

        var first = await f.Sink.CaptureAsync(request, Ct);
        var retry = await f.Sink.CaptureAsync(request, Ct);
        var changed = await f.Sink.CaptureAsync(request with { Answers = new Dictionary<string, string>
            { [f.Key] = """{"latitude":-34.603723,"longitude":-58.381592,"accuracyMeters":15}""" } }, Ct);

        Assert.Equal("Completed", first.State);
        Assert.Equal(first, retry);
        Assert.Equal("Rejected", changed.State);
        Assert.Equal(FormRecordErrors.ReplayConflict.Code, changed.ErrorCode);
        Assert.Single(f.Repository.Records);
        Assert.Equal(3, f.Repository.Values.Count);
    }

    private static void Rejected(BusinessFormCaptureFixture f, StructuredRecordCaptureResponse result)
    {
        Assert.Equal("Rejected", result.State);
        Assert.Equal(FormRecordErrors.InvalidResponse.Code, result.ErrorCode);
        Assert.Empty(f.Repository.Records);
        Assert.Empty(f.Repository.Values);
        Assert.Equal(0, f.Definition.LastRecordNumber);
        Assert.Equal("Rejected", Assert.Single(f.Repository.Receipts).State);
    }
}
