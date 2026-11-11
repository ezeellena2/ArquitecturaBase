using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// UB4-A: a point is one datum. The sheet, the detail and a correction read it from the response; filters and rules only ask
// whether it was answered, and the sheet does not sort by it.
public sealed class FormLocationReadTests
{
    private static readonly Guid FieldId = Guid.Parse("01990000-0000-7000-8000-000000000901");

    [Fact]
    public void The_sheet_and_the_detail_read_the_point_with_its_accuracy()
    {
        var reply = FormRecordPresentationPolicy.Project(Row(new { latitude = -34.603722m, longitude = -58.381592m, accuracyMeters = 15 }), "es", Schema());

        var answer = Assert.Single(reply.Values);
        Assert.Equal("location", answer.Type);
        Assert.Equal(new StructuredRecordLocationValue(-34.603722m, -58.381592m, 15), answer.Value);
        Assert.Equal("-34.603722, -58.381592", ((StructuredRecordLocationValue)answer.Value!).Coordinates());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(answer, JsonSerializerOptions.Web));
        var value = json.RootElement.GetProperty("value");
        Assert.Equal(-34.603722m, value.GetProperty("latitude").GetDecimal());
        Assert.Equal(-58.381592m, value.GetProperty("longitude").GetDecimal());
        Assert.Equal(15, value.GetProperty("accuracyMeters").GetInt32());
    }

    [Fact]
    public void A_point_without_accuracy_reads_without_it()
    {
        var reply = FormRecordPresentationPolicy.Project(Row(new { latitude = 90m, longitude = -180m }), "en", Schema());

        var location = Assert.IsType<StructuredRecordLocationValue>(Assert.Single(reply.Values).Value);
        Assert.Null(location.AccuracyMeters);
        Assert.Equal("90.000000, -180.000000", location.Coordinates());
        Assert.DoesNotContain("accuracyMeters", JsonSerializer.Serialize(location, JsonSerializerOptions.Web), StringComparison.Ordinal);
        Assert.Equal("StructuredRecordLocationValue { Content = [REDACTED] }", location.ToString());
    }

    [Fact]
    public void A_cell_is_answered_unanswered_or_not_asked()
    {
        var column = new FormResponseColumn(FieldId, "location", "Dónde estás", 1, []);
        var answered = FormRecordPresentationPolicy.Project(Row(new { latitude = 1m, longitude = 2m }), "es", Schema());
        var empty = FormRecordPresentationPolicy.Project(Row(null), "es", Schema());
        var older = FormRecordPresentationPolicy.Project(Row(null) with { SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [])) },
            "es", new FormVersionSchema(1, []));

        Assert.Equal("Answered", Assert.Single(FormResponsePresentationPolicy.Cells([column], answered)).State);
        Assert.Equal("Unanswered", Assert.Single(FormResponsePresentationPolicy.Cells([column], empty)).State);
        Assert.Equal("NotAsked", Assert.Single(FormResponsePresentationPolicy.Cells([column], older)).State);
    }

    [Fact]
    public void A_correction_fills_the_form_again_with_the_point_as_the_form_sends_it()
    {
        var canonical = FormLocationPolicy.Canonical(new(-34.603722m, -58.381592m, 15));

        Assert.Equal("""{"latitude":-34.603722,"longitude":-58.381592,"accuracyMeters":15}""", canonical);
        Assert.Equal(new StructuredRecordLocationValue(-34.603722m, -58.381592m, 15), FormLocationPolicy.Parse(canonical));
        Assert.Equal("""{"latitude":1,"longitude":2}""", FormLocationPolicy.Canonical(new(1m, 2m)));
    }

    [Fact]
    public void Filters_and_rules_only_ask_whether_it_was_answered()
    {
        var definition = Definition();
        foreach (var presence in new[] { "answered", "unanswered", "notAsked" })
            Assert.True(FormRecordFilterPolicy.Validate(new() { Conditions = [new(FieldId, presence, null)] }, definition).IsSuccess);
        foreach (var (operation, value) in new[] { ("equals", "-34.603722"), ("gte", "1"), ("contains", "-34"), ("in", "true") })
            Assert.True(FormRecordFilterPolicy.Validate(new() { Conditions = [new(FieldId, operation, [value])] }, definition).IsFailure);

        var snapshot = Snapshot(Row(new { latitude = 1m, longitude = 2m }));
        Assert.True(FormRecordConditionEvaluator.Matches([new(FieldId, "answered", null)], snapshot));
        Assert.False(FormRecordConditionEvaluator.Matches([new(FieldId, "unanswered", null)], snapshot));
        Assert.True(FormRecordConditionEvaluator.Matches([new(FieldId, "unanswered", null)], Snapshot(Row(null))));
    }

    [Theory]
    [InlineData("field")]
    [InlineData("-field")]
    public void The_sheet_does_not_sort_by_a_location(string sort) =>
        Assert.True(FormRecordFilterPolicy.Validate(new() { Sort = sort, SortFieldId = FieldId }, Definition()).IsFailure);

    private static CanonicalFormField Field() =>
        new(FieldId, "field_" + FieldId.ToString("N"), "location", new Dictionary<string, string> { ["es"] = "Dónde estás", ["en"] = "Where you are" },
            null, false, null, null);

    private static FormVersionSchema Schema() => new(1, [Field()]);

    private static FormRecordStoredRow Row(object? location) => new(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "VIS", Guid.NewGuid(), 1,
        JsonSerializer.Serialize(new Dictionary<string, string> { ["es"] = "Visita", ["en"] = "Visit" }),
        JsonSerializer.Serialize(Schema(), JsonSerializerOptions.Web),
        location is null ? "[]" : JsonSerializer.Serialize(new[] { new { fieldId = FieldId, value = location } }));

    private static FormResponseDefinitionRow Definition()
    {
        var row = Row(null);
        var version = new FormResponseVersionRow(row.VersionId, 1, row.NameJson, row.SchemaJson, "es", ["es", "en"]);
        return new(row.DefinitionId, row.Prefix, version.Id, [version]);
    }

    private static FormActionRecordRow Snapshot(FormRecordStoredRow row) => new(new(row,
        new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc), new(Guid.NewGuid(), "Author")), "es");
}
