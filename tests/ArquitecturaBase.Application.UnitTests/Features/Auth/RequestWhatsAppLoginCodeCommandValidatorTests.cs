using ArquitecturaBase.Application.Features.Auth.RequestWhatsAppLoginCode;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class RequestWhatsAppLoginCodeCommandValidatorTests
{
    private static readonly RequestWhatsAppLoginCodeCommandValidator Validator = new();

    [Theory]
    [InlineData("AR", "11 2345-6789")]
    [InlineData("ar", "11 2345-6789")]
    [InlineData(null, "+54 9 11 2345 6789")]
    public void A_number_with_or_without_a_country_passes(string? country, string number)
    {
        // Que sea un celular lo decide el parser en el caso de uso, que responde Users.Phone.Invalid.
        Assert.True(Validator.Validate(new RequestWhatsAppLoginCodeCommand(country, number)).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_number_is_required(string? number)
    {
        using var culture = new CultureScope("es");

        var failure = Assert.Single(Validator.Validate(new RequestWhatsAppLoginCodeCommand("AR", number)).Errors);

        Assert.Equal(nameof(RequestWhatsAppLoginCodeCommand.Number), failure.PropertyName);
        Assert.Equal("Este campo es obligatorio.", failure.ErrorMessage);
    }

    [Fact]
    public void The_number_has_a_maximum_length()
    {
        var number = new string('1', RequestWhatsAppLoginCodeCommandValidator.NumberMaxLength + 1);

        var failure = Assert.Single(Validator.Validate(new RequestWhatsAppLoginCodeCommand("AR", number)).Errors);

        Assert.Equal(nameof(RequestWhatsAppLoginCodeCommand.Number), failure.PropertyName);
    }

    [Theory]
    [InlineData("ARG")]
    [InlineData("A")]
    [InlineData("1A")]
    [InlineData("")]
    public void The_country_is_a_two_letter_code(string country)
    {
        using var culture = new CultureScope("es");

        var failure = Assert.Single(Validator.Validate(new RequestWhatsAppLoginCodeCommand(country, "11 2345-6789")).Errors);

        Assert.Equal(nameof(RequestWhatsAppLoginCodeCommand.Country), failure.PropertyName);
        Assert.Equal("Elegí un país de la lista.", failure.ErrorMessage);
    }
}
