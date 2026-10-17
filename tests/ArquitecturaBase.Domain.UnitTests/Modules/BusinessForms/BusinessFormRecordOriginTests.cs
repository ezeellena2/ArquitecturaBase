using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormRecordOriginTests
{
    [Fact]
    public void Capture_origin_is_sealed_once_and_preserves_the_original_fact()
    {
        var record = Record(); var source = Guid.NewGuid(); var suggestion = Guid.NewGuid();
        record.SetCaptureOrigin(source, suggestion, "{\"first\":true}");
        Assert.Throws<InvalidOperationException>(() => record.SetCaptureOrigin(Guid.NewGuid(), Guid.NewGuid(), "{\"second\":true}"));
        Assert.Equal(source, record.OriginRecordId); Assert.Equal(suggestion, record.OriginSuggestionId);
        Assert.Equal("{\"first\":true}", record.CaptureOriginJson);
    }

    [Fact]
    public void Suggestion_origin_requires_a_complete_nonempty_pair_and_capture_content()
    {
        var record = Record();
        Assert.Throws<ArgumentException>(() => record.SetCaptureOrigin(Guid.NewGuid(), null, "{}"));
        Assert.Throws<ArgumentException>(() => record.SetCaptureOrigin(null, Guid.NewGuid(), "{}"));
        Assert.Throws<ArgumentException>(() => record.SetCaptureOrigin(Guid.Empty, Guid.Empty, "{}"));
        Assert.Throws<ArgumentException>(() => record.SetCaptureOrigin(null, null, " "));
        Assert.Throws<ArgumentNullException>(() => record.SetCaptureOrigin(null, null, null!));
        Assert.Null(record.CaptureOriginJson); Assert.Null(record.OriginRecordId); Assert.Null(record.OriginSuggestionId);
        record.SetCaptureOrigin(null, null, "{}");
        Assert.Equal("{}", record.CaptureOriginJson);
    }

    private static BusinessFormRecord Record() => BusinessFormRecord.Create(Guid.NewGuid(), Guid.NewGuid(), 1,
        Guid.NewGuid(), Guid.NewGuid(), new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc), "[]");
}
