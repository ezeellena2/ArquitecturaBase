using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Connection;

public sealed class CuitNumberTests
{
    [Theory]
    [InlineData("30-71000123-1", "30710001231")]
    [InlineData("30710001231", "30710001231")]
    [InlineData(" 30 71000123 1 ", "30710001231")]
    public void A_valid_cuit_is_normalized_to_eleven_digits(string input, string expected) =>
        Assert.Equal(expected, CuitNumber.Normalize(input));

    [Theory]
    [InlineData("30-71000123-2")]
    [InlineData("3071000123")]
    [InlineData("307100012312")]
    [InlineData("30-7100012A-1")]
    [InlineData("")]
    [InlineData(null)]
    public void An_invalid_cuit_is_rejected(string? input) => Assert.Null(CuitNumber.Normalize(input));

    [Fact]
    public void A_check_digit_of_ten_is_never_valid()
    {
        // 20-00000001: the weighted sum leaves 11 - 1 = 10, a digit ARCA never assigns (it changes the prefix instead), so
        // no last digit makes it valid. A remainder of 11 maps to 0.
        Assert.All(Enumerable.Range(0, 10), digit => Assert.Null(CuitNumber.Normalize($"2000000001{digit}")));
        Assert.Equal("20000000060", CuitNumber.Normalize("20-00000006-0"));
    }

    [Fact]
    public void A_masked_cuit_keeps_only_the_prefix_and_the_last_digits()
    {
        Assert.Equal("30-*****123-1", CuitNumber.Mask("30710001231"));
        Assert.Equal("***", CuitNumber.Mask("123"));
    }
}
