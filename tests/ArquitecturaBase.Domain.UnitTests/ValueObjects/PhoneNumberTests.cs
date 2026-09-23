using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.UnitTests.ValueObjects;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("+5491123456789", "+5491123456789")]
    [InlineData("  +5493413654813  ", "+5493413654813")]
    [InlineData("+59899123456", "+59899123456")]
    [InlineData("+12345678", "+12345678")]
    [InlineData("+123456789012345", "+123456789012345")]
    public void International_numbers_are_accepted_without_surrounding_spaces(string value, string expected)
    {
        var result = PhoneNumber.Create(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
        Assert.Equal(expected, result.Value.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+")]
    [InlineData("5491123456789")]
    [InlineData("+05491123456789")]
    [InlineData("+1234567")]
    [InlineData("+1234567890123456")]
    [InlineData("+54 9 11 2345 6789")]
    [InlineData("+54-9-11-2345-6789")]
    [InlineData("+549112345678a")]
    [InlineData("++5491123456789")]
    [InlineData("+54911234567٨٩")]
    public void Anything_else_is_rejected(string? value)
    {
        var result = PhoneNumber.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.PhoneInvalidCode, result.Error.Code);
    }

    [Fact]
    public void The_longest_accepted_number_fits_in_the_max_length()
    {
        Assert.Equal(PhoneNumber.MaxLength, PhoneNumber.Create("+123456789012345").Value.Value.Length);
    }

    [Fact]
    public void Numbers_with_the_same_value_are_equal()
    {
        Assert.Equal(PhoneNumber.Create(" +5491123456789").Value, PhoneNumber.Create("+5491123456789").Value);
        Assert.NotEqual(PhoneNumber.Create("+5491123456789").Value, PhoneNumber.Create("+5491123456780").Value);
    }
}
