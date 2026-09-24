using ArquitecturaBase.Application.Configuration.Auth;
using System.ComponentModel.DataAnnotations;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Security;

public sealed class LoginCodeSecurityTests
{
    private const LoginCodePurpose SignIn = LoginCodePurpose.SignIn;

    private static readonly LoginCodeDestination Ana = LoginCodeDestination.ForEmail(Email.Create("ana@example.com").Value);
    private static readonly LoginCodeDestination Beto = LoginCodeDestination.ForEmail(Email.Create("beto@example.com").Value);
    private static readonly LoginCodeDestination AnaPhone = LoginCodeDestination.ForPhone(PhoneNumber.Create("+5491123456789").Value);

    [Fact]
    public void Codes_have_the_configured_number_of_digits_and_vary()
    {
        var generator = new LoginCodeGenerator(Options.Create(new LoginCodeOptions { Length = 8 }));

        var codes = Enumerable.Range(0, 1000).Select(_ => generator.Generate()).ToList();

        Assert.All(codes, code => Assert.Matches("^[0-9]{8}$", code));
        Assert.True(codes.Distinct().Count() > 990);
    }

    [Fact]
    public void Hash_is_stable_hexadecimal_and_does_not_contain_the_code()
    {
        var hasher = Hasher(ApiFactory.TestHashKey);

        var hash = hasher.Hash(Ana, SignIn, "123456");

        Assert.Equal(hash, hasher.Hash(Ana, SignIn, "123456"));
        Assert.Matches("^[0-9A-F]{64}$", hash);
        Assert.DoesNotContain("123456", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Hash_depends_on_the_email_the_code_and_the_key()
    {
        var hash = Hasher(ApiFactory.TestHashKey).Hash(Ana, SignIn, "123456");

        Assert.NotEqual(hash, Hasher(ApiFactory.TestHashKey).Hash(Beto, SignIn, "123456"));
        Assert.NotEqual(hash, Hasher(ApiFactory.TestHashKey).Hash(Ana, SignIn, "123457"));
        Assert.NotEqual(hash, Hasher(Convert.ToBase64String(new byte[32])).Hash(Ana, SignIn, "123456"));
    }

    [Fact]
    public void Hash_depends_on_the_purpose_and_on_the_number()
    {
        var hasher = Hasher(ApiFactory.TestHashKey);

        var hash = hasher.Hash(AnaPhone, SignIn, "123456");

        // Un código para vincular el número no sirve para entrar con él, ni al revés, aunque sean los mismos dígitos.
        Assert.NotEqual(hash, hasher.Hash(AnaPhone, LoginCodePurpose.VerifyDestination, "123456"));
        Assert.NotEqual(hash, hasher.Hash(LoginCodeDestination.ForPhone(PhoneNumber.Create("+5491123456780").Value), SignIn, "123456"));
        Assert.NotEqual(hash, hasher.Hash(Ana, SignIn, "123456"));
        Assert.Matches("^[0-9A-F]{64}$", hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==")]
    public void Missing_invalid_or_short_keys_are_rejected(string key)
    {
        var options = new LoginCodeHashOptions { HashKey = key };

        Assert.False(Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true));
    }

    private static LoginCodeHasher Hasher(string key) => new(Options.Create(new LoginCodeHashOptions { HashKey = key }));
}
