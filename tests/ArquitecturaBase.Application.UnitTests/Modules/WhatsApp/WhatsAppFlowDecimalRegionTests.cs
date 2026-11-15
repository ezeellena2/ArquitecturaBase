using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// F1: the Flow reads a number with the region of the language of the send, as the app does; a text that reads one way as is and another
// with the grouping of the language is an error of its question, not a silent 1.5.
public sealed class WhatsAppFlowDecimalRegionTests
{
    private static readonly WhatsAppFlowFieldMapping Mapping = new(Guid.CreateVersion7(), "field_x", "decimal", true, null, []);

    [Theory]
    [InlineData("es", "1.500,5", "1500.5")]
    [InlineData("es", "1.500.000", "1500000")]
    [InlineData("es", "1,5", "1.5")]
    [InlineData("es", "1500", "1500")]
    [InlineData("es", "-1.234,56", "-1234.56")]
    [InlineData("en", "1,500.25", "1500.25")]
    [InlineData("en", "1,500,000", "1500000")]
    [InlineData("en", "1.5", "1.5")]
    [InlineData("en", "1500", "1500")]
    public void A_number_is_read_with_the_region_of_the_language(string culture, string input, string expected)
    {
        var result = WhatsAppFlowValueCodec.Decode(Mapping, input, culture);
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Decimal);
    }

    [Theory]
    [InlineData("es", "1.500")]
    [InlineData("es", "-1.000")]
    [InlineData("en", "1,500")]
    [InlineData("en", "-12,000")]
    public void A_number_that_reads_two_ways_is_ambiguous(string culture, string input)
    {
        var result = WhatsAppFlowValueCodec.Decode(Mapping, input, culture);
        Assert.True(result.IsFailure);
        Assert.Equal(FlowErrors.NumberAmbiguous.Code, result.Error.Code);
    }

    [Theory]
    [InlineData("es", "1,500.25")]
    [InlineData("en", "1.500,25")]
    [InlineData("es", "1e2")]
    public void Other_malformed_numbers_stay_a_generic_invalid_value(string culture, string input)
    {
        var result = WhatsAppFlowValueCodec.Decode(Mapping, input, culture);
        Assert.True(result.IsFailure);
        Assert.Equal(FlowErrors.InvalidValue.Code, result.Error.Code);
    }

    [Fact]
    public void Without_a_language_the_canonical_text_is_read_as_before()
    {
        Assert.Equal("1.5", WhatsAppFlowValueCodec.Decode(Mapping, "1.500").Value.Decimal);
    }
}
