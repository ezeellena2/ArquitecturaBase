using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.UnitTests.ValueObjects;

public sealed class EmailTests
{
    [Fact]
    public void Email_is_trimmed_and_lowercased()
    {
        var result = Email.Create("  Ana.Perez@Example.COM ");

        Assert.True(result.IsSuccess);
        Assert.Equal("ana.perez@example.com", result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ana")]
    [InlineData("@example.com")]
    [InlineData("ana@")]
    [InlineData("ana@@example.com")]
    [InlineData("ana@example")]
    [InlineData("ana maria@example.com")]
    [InlineData("ana@example.com.")]
    public void Invalid_emails_are_rejected(string? value)
    {
        var result = Email.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.EmailInvalidCode, result.Error.Code);
    }

    [Fact]
    public void Emails_longer_than_the_limit_are_rejected()
    {
        var value = new string('a', Email.MaxLength - "@example.com".Length + 1) + "@example.com";

        Assert.True(Email.Create(value).IsFailure);
    }

    [Fact]
    public void Emails_with_the_same_normalized_value_are_equal()
    {
        Assert.Equal(Email.Create("ANA@example.com").Value, Email.Create("ana@example.com").Value);
    }
}
