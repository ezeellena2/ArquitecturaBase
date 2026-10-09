using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class AiSecretProtectionTests
{
    [Fact]
    public void Existing_guided_receipts_keep_their_original_fingerprint_when_no_place_is_supplied()
    {
        var ring = new AiKeyRing(Options.Create(Ring()));
        var request = new UpdateAiBusinessInformationRequest("Shop", "Repair", "", "", "", "", "", 7,
            Guid.Parse("01912345-1234-7000-8000-123456789012"), new(new("local", "Address", "", "", -34.6037, -58.3816),
                new("UTC", Enumerable.Range(0, 7).Select(i => new BusinessOpeningDay(i, "unknown", [])).ToArray(), []), []));
        var normalized = request.Details!.Normalize().Value;
        // The deployed details:v1 schema, before PlaceId existed.
        var oldDetails = new { Location = new { normalized.Location.Mode, normalized.Location.Address, normalized.Location.Reference,
            normalized.Location.Area, normalized.Location.Latitude, normalized.Location.Longitude, normalized.Location.Link }, normalized.OpeningHours, normalized.Faq };
        object[] oldValues = ["Shop", "Repair", "", "", "", "", "", 7L, request.RequestId, "details:v1", oldDetails];
        var expected = Convert.ToBase64String(System.Security.Cryptography.HMACSHA256.HashData(ring.BusinessInformationFingerprintKey("one"),
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(oldValues)));
        var fingerprint = new HmacAiBusinessInformationFingerprint(ring);
        Assert.Equal(expected, fingerprint.Create(request, "one"));
        var selected = request with { Details = normalized with { Location = normalized.Location with { Latitude = null, Longitude = null, PlaceId = "ChIJ_one" } } };
        Assert.False(fingerprint.Matches(selected, "one", expected));
        Assert.False(fingerprint.Matches(selected with { Details = selected.Details! with { Location = selected.Details!.Location with { PlaceId = "ChIJ_two" } } }, "one", fingerprint.Create(selected, "one")));
    }

    private static AiSecretProtectionOptions Ring(string active = "one", string installation = "test-installation") => new()
    {
        InstallationId = installation, ActiveKeyId = active,
        Keys = new Dictionary<string, string> { ["one"] = Convert.ToBase64String(new byte[32]), ["two"] = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray()) },
    };
    private static AesGcmAiSecretProtector Protector(AiSecretProtectionOptions options) => new(new AiKeyRing(Options.Create(options)));

    [Fact]
    public void Ciphertext_authenticates_the_provider_as_well_as_the_installation()
    {
        var protector = Protector(Ring());
        var encrypted = protector.Protect(AiProvider.OpenAI, "fictional-key");
        Assert.Equal("fictional-key", protector.Unprotect(AiProvider.OpenAI, encrypted));
        Assert.Null(protector.Unprotect(AiProvider.Anthropic, encrypted));
    }

    [Fact]
    public void Encryption_is_randomized_and_authenticates_the_installation_and_envelope()
    {
        var protector = Protector(Ring());
        var first = protector.Protect(AiProvider.OpenAI, "fictional-api-key");
        var second = protector.Protect(AiProvider.OpenAI, "fictional-api-key");
        Assert.NotEqual(first, second);
        Assert.DoesNotContain("fictional-api-key", first, StringComparison.Ordinal);
        Assert.Equal("fictional-api-key", protector.Unprotect(AiProvider.OpenAI, first));
        Assert.Null(Protector(Ring(installation: "another-installation")).Unprotect(AiProvider.OpenAI, first));
        Assert.Null(protector.Unprotect(AiProvider.OpenAI, first[..^3] + "abc"));
    }

    [Fact]
    public void Rotation_reads_old_ciphertext_and_fingerprints_with_their_original_key()
    {
        var oldRing = new AiKeyRing(Options.Create(Ring()));
        var rotatedRing = new AiKeyRing(Options.Create(Ring("two")));
        var encrypted = new AesGcmAiSecretProtector(oldRing).Protect(AiProvider.OpenAI, "fictional-key");
        Assert.Equal("fictional-key", new AesGcmAiSecretProtector(rotatedRing).Unprotect(AiProvider.OpenAI, encrypted));
        var request = new UpdateAiConnectionRequest(AiProvider.OpenAI, "model", AiCredentialChange.Replace, "fictional-key", true, 0, Guid.NewGuid());
        var fingerprint = new HmacAiChangeFingerprint(oldRing).Create(request, "one");
        Assert.True(new HmacAiChangeFingerprint(rotatedRing).Matches(request, "one", fingerprint));
        Assert.False(new HmacAiChangeFingerprint(rotatedRing).Matches(request with { ApiKey = "other-key" }, "one", fingerprint));
        var businessRequest = new UpdateAiBusinessInformationRequest(" Shop ", " Repairs ", "", "", "", "", "", 0, Guid.NewGuid());
        var businessFingerprint = new HmacAiBusinessInformationFingerprint(oldRing).Create(businessRequest, "one");
        var restoredBusiness = new HmacAiBusinessInformationFingerprint(rotatedRing);
        Assert.True(restoredBusiness.Matches(businessRequest with { Name = "Shop", Description = "Repairs" }, "one", businessFingerprint));
        Assert.False(restoredBusiness.Matches(businessRequest with { Hours = "10–17" }, "one", businessFingerprint));
        Assert.False(new HmacAiBusinessInformationFingerprint(new AiKeyRing(Options.Create(Ring(installation: "another-installation"))))
            .Matches(businessRequest, "one", businessFingerprint));
        Assert.NotEqual(Convert.ToBase64String(oldRing.FingerprintKey("one")), Convert.ToBase64String(oldRing.BusinessInformationFingerprintKey("one")));
        Assert.Throws<InvalidOperationException>(() => new HmacAiBusinessInformationFingerprint(new AiKeyRing(
            Options.Create(Ring() with { Keys = new Dictionary<string, string>() }))).Matches(businessRequest, "one", businessFingerprint));
        Assert.Null(Protector(Ring() with { Keys = new Dictionary<string, string>() }).Unprotect(AiProvider.OpenAI, encrypted));
    }
}
