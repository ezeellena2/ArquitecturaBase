using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFlowResponseLimitsTests
{
    private const string Key = "field_0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(Key, true, null)]
    [InlineData(Key + "_r0", true, 0)]
    [InlineData(Key + "_r1", true, 1)]
    [InlineData(Key + "_r10", true, 10)]
    [InlineData(Key + "_r11", false, null)]
    [InlineData(Key + "_r01", false, null)]
    [InlineData(Key + "_r+1", false, null)]
    [InlineData(Key + "_r", false, null)]
    [InlineData(Key + "_", false, null)]
    [InlineData(Key + "_x1", false, null)]
    [InlineData("field_0123456789abcdef_r1", false, null)]
    [InlineData("flow_token", false, null)]
    public void A_row_answer_comes_under_its_question_key_with_the_row_up_to_ten(string name, bool valid, int? row)
    {
        Assert.Equal(valid, WhatsAppFlowResponseLimits.IsFieldKey(name, out var parsed));
        Assert.Equal(row, parsed);
    }

    [Theory]
    [InlineData("{\"flow_token\":\"t\",\"" + Key + "_r1\":\"a\"}", true)]
    [InlineData("{\"" + Key + "_r0\":\"false\"}", true)]
    [InlineData("{\"flow_token\":\"t\",\"" + Key + "\":\"a\"}", false)]
    [InlineData("{\"" + Key + "_r11\":\"a\"}", false)]
    [InlineData("not json", false)]
    public void A_response_has_rows_when_a_key_names_one(string json, bool expected) =>
        Assert.Equal(expected, WhatsAppFlowResponseLimits.HasRows(json));

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
