using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFlowResponseLimitsTests
{
    [Theory]
    [InlineData("{\"field_choices\":[\"a\",\"b\"]}", null)]
    [InlineData("{\"field_choices\":[\"a\"],\"field_photo\":[{},{}]}", 2)]
    [InlineData("{\"field_choices\":[\"a\"],\"field_photo\":[]}", 0)]
    [InlineData("{\"field_photo\":[]}", 0)]
    [InlineData("{\"field_photo\":[{},{}],\"field_document\":[{}]}", 3)]
    [InlineData("{\"field_mixed\":[{},\"a\"]}", null)]
    [InlineData("{\"field_mixed\":[{},\"a\"],\"field_photo\":[{}]}", 1)]
    [InlineData("{\"field_text\":\"text\"}", null)]
    public void The_preliminary_media_budget_counts_only_object_arrays_and_preserves_ambiguous_empty_lists(string json, int? expected)
    {
        Assert.Equal(expected, WhatsAppFlowResponseLimits.CountFiles(json));
        Assert.Equal(expected.HasValue, WhatsAppFlowResponseLimits.HasFileLists(json));
    }
}
