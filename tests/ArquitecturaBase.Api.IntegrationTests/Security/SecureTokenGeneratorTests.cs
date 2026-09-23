using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Infrastructure.Security;

namespace ArquitecturaBase.Api.IntegrationTests.Security;

/// <summary>Los tokens del enlace de ingreso (sección 6.4 del spec del ingreso con WhatsApp).</summary>
public sealed class SecureTokenGeneratorTests
{
    private readonly SecureTokenGenerator _generator = new();

    [Fact]
    public void Tokens_are_32_random_bytes_in_base64url_without_padding()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => _generator.Generate()).ToList();

        Assert.All(tokens, token =>
        {
            Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
            Assert.Equal(32, Base64Url.DecodeFromChars(token).Length);
        });
        Assert.Equal(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Hash_is_the_sha256_of_the_token_in_upper_case_hexadecimal()
    {
        var token = _generator.Generate();

        var hash = _generator.Hash(token);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), hash);
        Assert.Matches("^[0-9A-F]{64}$", hash);
        Assert.Equal(hash, _generator.Hash(token));
        Assert.NotEqual(hash, _generator.Hash(_generator.Generate()));
    }

    [Fact]
    public void Generated_tokens_have_the_token_format()
    {
        Assert.Equal(43, ISecureTokenGenerator.TokenLength);
        Assert.True(ISecureTokenGenerator.HasTokenFormat(_generator.Generate()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAá")]
    public void Anything_else_does_not_have_the_token_format(string? text)
    {
        Assert.False(ISecureTokenGenerator.HasTokenFormat(text));
    }
}
