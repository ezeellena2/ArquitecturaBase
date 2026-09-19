using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;

namespace ArquitecturaBase.Application.UnitTests.Common.Validation;

public sealed class ValidationRulesTests
{
    private sealed record SignUp(string? Name, string? Email);

    private sealed class SignUpValidator : AbstractValidator<SignUp>
    {
        public SignUpValidator()
        {
            RuleFor(signUp => signUp.Name).Required().MaxLength(5);
            RuleFor(signUp => signUp.Email).ValidEmail();
        }
    }

    [Fact]
    public void Required_uses_the_translated_message()
    {
        using var culture = new CultureScope("es");

        var result = new SignUpValidator().Validate(new SignUp("", "ana@example.com"));

        var failure = Assert.Single(result.Errors);
        Assert.Equal("Name", failure.PropertyName);
        Assert.Equal("Este campo es obligatorio.", failure.ErrorMessage);
    }

    [Fact]
    public void Max_length_message_includes_the_limit()
    {
        using var culture = new CultureScope("en");

        var result = new SignUpValidator().Validate(new SignUp("abcdefgh", "ana@example.com"));

        Assert.Equal("Enter at most 5 characters.", Assert.Single(result.Errors).ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Missing_email_only_reports_required(string? email)
    {
        using var culture = new CultureScope("es");

        var result = new SignUpValidator().Validate(new SignUp("Ana", email));

        Assert.Equal("Este campo es obligatorio.", Assert.Single(result.Errors).ErrorMessage);
    }

    [Theory]
    [InlineData("ana")]
    [InlineData("ana@")]
    public void Invalid_email_is_rejected(string email)
    {
        using var culture = new CultureScope("es");

        var result = new SignUpValidator().Validate(new SignUp("Ana", email));

        Assert.Equal("Ingresá un correo válido.", Assert.Single(result.Errors).ErrorMessage);
    }

    [Fact]
    public void Valid_data_passes()
    {
        Assert.True(new SignUpValidator().Validate(new SignUp("Ana", "ana@example.com")).IsValid);
    }
}
