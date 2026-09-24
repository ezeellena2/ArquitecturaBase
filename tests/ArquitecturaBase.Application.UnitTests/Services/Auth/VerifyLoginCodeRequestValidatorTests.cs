using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Validation.Auth;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

public sealed class VerifyLoginCodeRequestValidatorTests
{
    private static readonly VerifyLoginCodeRequestValidator Validator = new(Options.Create(new LoginCodeOptions()));

    [Fact]
    public void Valid_command_passes()
    {
        Assert.True(Validator.Validate(new VerifyLoginCodeRequest("ana@example.com", "123456", "/connect/authorize?x=1")).IsValid);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void Code_must_have_the_configured_digits(string code)
    {
        using var culture = new CultureScope("es");

        var failure = Assert.Single(Validator.Validate(new VerifyLoginCodeRequest("ana@example.com", code, "/connect/authorize")).Errors);

        Assert.Equal(nameof(VerifyLoginCodeRequest.Code), failure.PropertyName);
        Assert.Equal("Ingresá el código que te enviamos por email.", failure.ErrorMessage);
    }

    [Fact]
    public void A_phone_instead_of_an_email_passes()
    {
        // El formato del número lo controla el caso de uso, que responde Users.Phone.Invalid.
        Assert.True(Validator.Validate(new VerifyLoginCodeRequest(null, "123456", "/connect/authorize", Phone: "+5491123456789")).IsValid);
    }

    [Fact]
    public void Email_and_phone_together_are_rejected_on_the_phone()
    {
        using var culture = new CultureScope("es");

        var failure = Assert.Single(Validator.Validate(
            new VerifyLoginCodeRequest("ana@example.com", "123456", "/connect/authorize", Phone: "+5491123456789")).Errors);

        Assert.Equal(nameof(VerifyLoginCodeRequest.Phone), failure.PropertyName);
        Assert.Equal("Mandá el correo o el número, no los dos.", failure.ErrorMessage);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(null, " ")]
    public void Without_email_or_phone_the_email_is_required(string? email, string? phone)
    {
        using var culture = new CultureScope("es");

        var failure = Assert.Single(Validator.Validate(
            new VerifyLoginCodeRequest(email, "123456", "/connect/authorize", phone)).Errors);

        Assert.Equal(nameof(VerifyLoginCodeRequest.Email), failure.PropertyName);
        Assert.Equal("Este campo es obligatorio.", failure.ErrorMessage);
    }

    [Fact]
    public void A_code_sent_by_whatsapp_is_asked_for_as_such()
    {
        using var culture = new CultureScope("es");

        var failure = Assert.Single(Validator.Validate(
            new VerifyLoginCodeRequest(null, "12", "/connect/authorize", Phone: "+5491123456789")).Errors);

        Assert.Equal(nameof(VerifyLoginCodeRequest.Code), failure.PropertyName);
        Assert.Equal("Ingresá el código que te enviamos por WhatsApp.", failure.ErrorMessage);
    }

    [Theory]
    [InlineData("https://evil.example/connect/authorize")]
    [InlineData("/login")]
    public void Return_url_must_be_the_local_authorize_endpoint(string returnUrl)
    {
        using var culture = new CultureScope("es");

        var failure = Assert.Single(Validator.Validate(new VerifyLoginCodeRequest("ana@example.com", "123456", returnUrl)).Errors);

        Assert.Equal(nameof(VerifyLoginCodeRequest.ReturnUrl), failure.PropertyName);
        Assert.Equal("La dirección de retorno no es válida.", failure.ErrorMessage);
    }
}
