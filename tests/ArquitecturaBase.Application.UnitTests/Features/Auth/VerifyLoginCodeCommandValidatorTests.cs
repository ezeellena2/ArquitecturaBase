using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.Features.Auth;

public sealed class VerifyLoginCodeCommandValidatorTests
{
    private static readonly VerifyLoginCodeCommandValidator Validator = new(Options.Create(new LoginCodeOptions()));

    [Fact]
    public void Valid_command_passes()
    {
        Assert.True(Validator.Validate(new VerifyLoginCodeCommand("ana@example.com", "123456", "/connect/authorize?x=1")).IsValid);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void Code_must_have_the_configured_digits(string code)
    {
        using var culture = new CultureScope("es");

        var failure = Assert.Single(Validator.Validate(new VerifyLoginCodeCommand("ana@example.com", code, "/connect/authorize")).Errors);

        Assert.Equal(nameof(VerifyLoginCodeCommand.Code), failure.PropertyName);
        Assert.Equal("Ingresá el código que te enviamos por email.", failure.ErrorMessage);
    }

    [Theory]
    [InlineData("https://evil.example/connect/authorize")]
    [InlineData("/login")]
    public void Return_url_must_be_the_local_authorize_endpoint(string returnUrl)
    {
        using var culture = new CultureScope("es");

        var failure = Assert.Single(Validator.Validate(new VerifyLoginCodeCommand("ana@example.com", "123456", returnUrl)).Errors);

        Assert.Equal(nameof(VerifyLoginCodeCommand.ReturnUrl), failure.PropertyName);
        Assert.Equal("La dirección de retorno no es válida.", failure.ErrorMessage);
    }
}
