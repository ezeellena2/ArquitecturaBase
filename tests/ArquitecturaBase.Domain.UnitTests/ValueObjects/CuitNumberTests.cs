using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.UnitTests.ValueObjects;

// DOC1: the CUIT or CUIL of the core, which the fixed validations of the forms use: eleven digits and the module-11 check digit.
public sealed class CuitNumberTests
{
    [Theory]
    [InlineData("20-12345678-6", "20123456786")]
    [InlineData("20123456786", "20123456786")]
    [InlineData(" 30 71000123 1 ", "30710001231")]
    [InlineData("27-40000123-0", "27400001230")]
    public void A_valid_cuit_or_cuil_is_normalized_to_eleven_digits(string input, string expected) =>
        Assert.Equal(expected, CuitNumber.Normalize(input));

    [Theory]
    [InlineData("20-12345678-7")]
    [InlineData("2012345678")]
    [InlineData("201234567861")]
    [InlineData("20-1234567A-6")]
    [InlineData("20.12345678.6")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anything_else_is_rejected(string? input) => Assert.Null(CuitNumber.Normalize(input));

    [Theory]
    // A no-break space, a narrow no-break space, a tab, an en dash and a minus sign, as a receipt or a copy and paste leaves them.
    [InlineData("20 12345678 6")]
    [InlineData("20 12345678 6")]
    [InlineData("20\t12345678\t6")]
    [InlineData("20–12345678–6")]
    [InlineData("20−12345678−6")]
    public void Any_space_or_dash_between_the_digits_is_a_separator(string input) => Assert.Equal("20123456786", CuitNumber.Normalize(input));

    [Fact]
    public void Eleven_zeros_add_up_but_are_no_cuit()
    {
        Assert.Null(CuitNumber.Normalize("00000000000"));
        Assert.Null(CuitNumber.Normalize("00-00000000-0"));
    }

    [Fact]
    public void A_check_digit_of_ten_is_never_valid_and_a_remainder_of_eleven_is_zero()
    {
        // 20-00000001: the weighted sum leaves 11 - 1 = 10, which is never assigned, so no last digit makes it valid.
        Assert.All(Enumerable.Range(0, 10), digit => Assert.Null(CuitNumber.Normalize($"2000000001{digit}")));
        Assert.Equal("20000000060", CuitNumber.Normalize("20-00000006-0"));
    }

    [Fact]
    public void A_masked_cuit_keeps_only_the_prefix_and_the_last_digits()
    {
        Assert.Equal("20-*****678-6", CuitNumber.Mask("20123456786"));
        Assert.Equal("***", CuitNumber.Mask("123"));
    }
}
