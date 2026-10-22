using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFlowMultipleChoiceCodecTests
{
    private static readonly Guid First = Guid.Parse("019a0000-0000-7000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("019a0000-0000-7000-8000-000000000002");

    [Fact]
    public void Multiple_choice_preserves_wire_order_and_an_explicit_empty_array()
    {
        var mapping = Mapping(0, 2);
        var selected = WhatsAppFlowValueCodec.Decode(mapping, JsonSerializer.Serialize(new[] { Second, First }));
        Assert.True(selected.IsSuccess);
        Assert.True(selected.Value.IsPresent);
        Assert.Equal([Second, First], selected.Value.ChoiceIds);
        Assert.Null(selected.Value.ChoiceId);
        var empty = WhatsAppFlowValueCodec.Decode(mapping, "[]");
        Assert.True(empty.IsSuccess);
        Assert.True(empty.Value.IsPresent);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<Guid>>(empty.Value.ChoiceIds));
        var missing = WhatsAppFlowValueCodec.Decode(mapping, null);
        Assert.True(missing.IsSuccess);
        Assert.False(missing.Value.IsPresent);
        Assert.Null(missing.Value.ChoiceIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("[\"019a0000-0000-7000-8000-000000000001\"]")]
    public void The_sealed_minimum_applies_to_required_and_present_multiple_choices(string? raw)
    {
        Assert.Equal(FlowErrors.InvalidValue, WhatsAppFlowValueCodec.Decode(Mapping(2, 2), raw).Error);
    }

    [Theory]
    [InlineData("[")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"019a0000-0000-7000-8000-000000000001\"")]
    [InlineData("[null]")]
    [InlineData("[1]")]
    [InlineData("[true]")]
    [InlineData("[{}]")]
    [InlineData("[\"019a0000000070008000000000000001\"]")]
    [InlineData("[\"00000000-0000-0000-0000-000000000000\"]")]
    [InlineData("[\"019a0000-0000-7000-8000-000000000003\"]")]
    [InlineData("[\"019a0000-0000-7000-8000-000000000001\",\"019a0000-0000-7000-8000-000000000001\"]")]
    [InlineData("[\"019a0000-0000-7000-8000-000000000001\",\"019a0000-0000-7000-8000-000000000002\"]")]
    public void Multiple_choice_rejects_wrong_shapes_duplicates_unoffered_ids_and_excess(string raw)
    {
        var result = WhatsAppFlowValueCodec.Decode(Mapping(0, 1), raw);
        Assert.True(result.IsFailure);
        Assert.Equal(FlowErrors.InvalidValue, result.Error);
    }

    [Fact]
    public void Twenty_offered_options_are_values_rather_than_a_file_budget()
    {
        var choices = Enumerable.Range(1, 20).Select(index => Guid.Parse($"019a0000-0000-7000-8000-{index:000000000000}")).ToArray();
        var mapping = Mapping(1, 20) with { Choices = choices };
        var result = WhatsAppFlowValueCodec.Decode(mapping, JsonSerializer.Serialize(choices));
        Assert.True(result.IsSuccess);
        Assert.Equal(choices, result.Value.ChoiceIds);
    }

    private static WhatsAppFlowFieldMapping Mapping(int minimum, int maximum) => new(First,
        "field_" + First.ToString("N"), "multiChoice", minimum > 0, null, [First, Second],
        Selection: new StructuredFormSelectionSettings(minimum, maximum));
}
