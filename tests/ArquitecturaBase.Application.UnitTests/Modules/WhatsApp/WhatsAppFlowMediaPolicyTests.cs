using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFlowMediaPolicyTests
{
    [Fact]
    public void Media_metadata_is_bound_to_the_sealed_question_and_order_without_bytes()
    {
        var projection = Projection(1, 2);
        var key = projection.Mapping[0].Key;
        var media = new Dictionary<string, string> { [key] = "[{\"id\":\"123\",\"file_name\":\"photo.png\",\"mime_type\":\"image/png\",\"sha256\":\"header-hash\"}]" };
        var result = WhatsAppFlowMediaPolicy.Decode(projection, new Dictionary<string, string>(), media);
        Assert.True(result.IsSuccess);
        var file = Assert.Single(result.Value);
        Assert.Equal(projection.Mapping[0].FieldId, file.FieldId);
        Assert.Equal(0, file.Position);
        Assert.Equal("photo.png", file.Name);
        Assert.Null(file.Content);
        Assert.DoesNotContain(file.Name, file.ToString());
    }
    [Fact]
    public void Missing_required_media_scalar_substitution_and_unknown_file_questions_are_rejected()
    {
        var projection = Projection(1, 2);
        Assert.True(WhatsAppFlowMediaPolicy.Decode(projection, new Dictionary<string, string>(), new Dictionary<string, string>()).IsFailure);
        Assert.True(WhatsAppFlowMediaPolicy.Decode(projection, new Dictionary<string, string> { [projection.Mapping[0].Key] = "[]" }, new Dictionary<string, string>()).IsFailure);
        Assert.True(WhatsAppFlowMediaPolicy.Decode(Projection(0, 2), new Dictionary<string, string>(), new Dictionary<string, string> { ["unknown"] = "[]" }).IsFailure);
        Assert.Empty(WhatsAppFlowMediaPolicy.Decode(Projection(0, 2), new Dictionary<string, string>(), new Dictionary<string, string>()).Value);
    }
    [Theory]
    [InlineData("{\"field_a\":[{},{}],\"field_b\":[]}", 2)]
    [InlineData("{\"field_a\":[]}", 0)]
    [InlineData("{\"field_a\":\"text\"}", null)]
    [InlineData("invalid", null)]
    public void The_public_progress_count_contains_only_array_lengths(string json, int? expected)
    {
        Assert.Equal(expected, ArquitecturaBase.Application.Modules.WhatsApp.Configuration.WhatsAppFlowResponseLimits.CountFiles(json));
    }
    private static WhatsAppFlowProjection Projection(int minimum, int maximum)
    {
        var id = Guid.CreateVersion7();
        return new(1, "{}", "hash", WhatsAppFlowCompiler.ProfileRevision, 1, "es", [new(id, "field_" + id.ToString("N"), "photo", minimum > 0, null, [], new StructuredFormFileSettings(minimum, maximum, "camera"))]);
    }
}
