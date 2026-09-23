using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.WhatsApp;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// La firma de los POST de Meta (<c>X-Hub-Signature-256</c>) y la palabra de verificación del GET (sección 7 del spec).
/// Son de unidad: no usan la base.
/// </summary>
public sealed class WhatsAppSignatureValidatorTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"object":"whatsapp_business_account","entry":[]}""");

    private readonly WhatsAppSignatureValidator _validator = new(Options.Create(new WhatsAppOptions
    {
        PhoneNumberId = ApiFactory.WhatsAppPhoneNumberId,
        AccessToken = "test-access-token",
        AppSecret = ApiFactory.WhatsAppAppSecret,
        VerifyToken = ApiFactory.WhatsAppVerifyToken,
    }));

    [Fact]
    public void The_hmac_of_the_raw_body_with_the_app_secret_is_valid()
    {
        Assert.True(_validator.IsValidSignature(Body, MetaWebhook.Sign(Body)));
    }

    [Fact]
    public void Hex_in_uppercase_is_valid_too()
    {
        var signature = MetaWebhook.Sign(Body);

        Assert.True(_validator.IsValidSignature(Body, "sha256=" + signature["sha256=".Length..].ToUpperInvariant()));
    }

    [Fact]
    public void A_body_that_changed_by_one_byte_is_not_valid()
    {
        var signature = MetaWebhook.Sign(Body);
        var changed = (byte[])Body.Clone();
        changed[^2] ^= 1;

        Assert.False(_validator.IsValidSignature(changed, signature));
    }

    [Fact]
    public void A_signature_made_with_another_secret_is_not_valid()
    {
        Assert.False(_validator.IsValidSignature(Body, MetaWebhook.Sign(Body, "another-app-secret")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=")]
    [InlineData("sha256=zz")]
    [InlineData("sha256=not-hex-at-all-not-hex-at-all-not-hex-at-all-not-hex-at-all!")]
    [InlineData("sha256=abc")]
    [InlineData("sha256=00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff00")]
    [InlineData("sha256=00112233445566778899aabbccddeeff00112233445566778899aabbccddee")]
    [InlineData("sha1=00112233445566778899aabbccddeeff00112233")]
    public void A_missing_or_malformed_header_is_not_valid(string? header)
    {
        Assert.False(_validator.IsValidSignature(Body, header));
    }

    /// <summary>La firma correcta con otro prefijo, o sin él, tampoco vale: el formato es "sha256=&lt;hex&gt;".</summary>
    [Fact]
    public void The_right_hmac_without_its_prefix_is_not_valid()
    {
        var hex = MetaWebhook.Sign(Body)["sha256=".Length..];

        Assert.False(_validator.IsValidSignature(Body, hex));
        Assert.False(_validator.IsValidSignature(Body, "sha512=" + hex));
    }

    [Fact]
    public void The_verify_token_must_match_exactly()
    {
        Assert.True(_validator.IsValidVerifyToken(ApiFactory.WhatsAppVerifyToken));

        Assert.False(_validator.IsValidVerifyToken(null));
        Assert.False(_validator.IsValidVerifyToken(string.Empty));
        Assert.False(_validator.IsValidVerifyToken(ApiFactory.WhatsAppVerifyToken + "x"));
        Assert.False(_validator.IsValidVerifyToken(ApiFactory.WhatsAppVerifyToken[..^1]));
        Assert.False(_validator.IsValidVerifyToken(ApiFactory.WhatsAppVerifyToken.ToUpperInvariant()));
    }
}
