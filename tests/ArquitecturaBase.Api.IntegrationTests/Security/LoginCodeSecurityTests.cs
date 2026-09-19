using System.ComponentModel.DataAnnotations;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Security;

public sealed class LoginCodeSecurityTests
{
    private static readonly Email Ana = Email.Create("ana@example.com").Value;
    private static readonly Email Beto = Email.Create("beto@example.com").Value;

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

        var hash = hasher.Hash(Ana, "123456");

        Assert.Equal(hash, hasher.Hash(Ana, "123456"));
        Assert.Matches("^[0-9A-F]{64}$", hash);
        Assert.DoesNotContain("123456", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Hash_depends_on_the_email_the_code_and_the_key()
    {
        var hash = Hasher(ApiFactory.TestHashKey).Hash(Ana, "123456");

        Assert.NotEqual(hash, Hasher(ApiFactory.TestHashKey).Hash(Beto, "123456"));
        Assert.NotEqual(hash, Hasher(ApiFactory.TestHashKey).Hash(Ana, "123457"));
        Assert.NotEqual(hash, Hasher(Convert.ToBase64String(new byte[32])).Hash(Ana, "123456"));
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
