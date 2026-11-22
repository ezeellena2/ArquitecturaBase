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

    // RC1-A: without the Home days a connection change keeps the receipt it had before they existed, so a retry across the deployment
    // replays the saved change instead of answering a conflict; with them, the days are part of what it compares.
    [Fact]
    public void Existing_connection_receipts_keep_their_original_fingerprint_without_the_home_days()
    {
        var ring = new AiKeyRing(Options.Create(Ring()));
        var request = new UpdateAiConnectionRequest(AiProvider.OpenAI, " model ", AiCredentialChange.Replace, "fictional-key", true, 7,
            Guid.Parse("01912345-1234-7000-8000-123456789012"));
        // The deployed payload, before the Home days existed.
        object?[] oldValues = ["OpenAI", "model", "Replace", "fictional-key", true, 7L, request.RequestId];
        var expected = Convert.ToBase64String(System.Security.Cryptography.HMACSHA256.HashData(ring.FingerprintKey("one"),
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(oldValues)));
        var fingerprint = new HmacAiChangeFingerprint(ring);
        Assert.Equal(expected, fingerprint.Create(request, "one"));
        var withDays = request with { ConversationRetentionDays = 7 };
        Assert.False(fingerprint.Matches(withDays, "one", expected));
        Assert.True(fingerprint.Matches(withDays, "one", fingerprint.Create(withDays, "one")));
        Assert.False(fingerprint.Matches(withDays with { ConversationRetentionDays = 8 }, "one", fingerprint.Create(withDays, "one")));
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

    // I7, review: probing a saved key agrees with reading it, for every reason a key cannot be read, without handing out its text.
    [Fact]
    public void Probing_a_saved_key_agrees_with_reading_it()
    {
        var protector = Protector(Ring());
        var encrypted = protector.Protect(AiProvider.OpenAI, "fictional-key");
        Assert.True(protector.CanUnprotect(AiProvider.OpenAI, encrypted));
        Assert.True(Protector(Ring("two")).CanUnprotect(AiProvider.OpenAI, encrypted));
        Assert.False(protector.CanUnprotect(AiProvider.Anthropic, encrypted));
        Assert.False(protector.CanUnprotect(AiProvider.OpenAI, encrypted[..^3] + "abc"));
        Assert.False(protector.CanUnprotect(AiProvider.OpenAI, "not-an-envelope"));
        Assert.False(Protector(Ring(installation: "another-installation")).CanUnprotect(AiProvider.OpenAI, encrypted));
        Assert.False(Protector(Ring() with { Keys = new Dictionary<string, string>() }).CanUnprotect(AiProvider.OpenAI, encrypted));
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
        Assert.Null(Protector(Ring() with { Keys = new Dictionary<string, string>() }).Unprotect(AiProvider.OpenAI, encrypted));
    }

    // I17: a receipt of a key that is no longer in the ring cannot be verified; it does not match, so its caller answers a conflict.
    [Fact]
    public void A_receipt_of_a_key_absent_from_the_ring_does_not_match_and_does_not_throw()
    {
        var oldRing = new AiKeyRing(Options.Create(Ring()));
        var withoutOne = new AiKeyRing(Options.Create(Ring("two") with { Keys = new Dictionary<string, string> { ["two"] = Ring().Keys["two"] } }));
        var connection = new UpdateAiConnectionRequest(AiProvider.OpenAI, "model", AiCredentialChange.Replace, "fictional-key", true, 0, Guid.NewGuid());
        Assert.False(new HmacAiChangeFingerprint(withoutOne).Matches(connection, "one", new HmacAiChangeFingerprint(oldRing).Create(connection, "one")));
        var business = new UpdateAiBusinessInformationRequest("Shop", "Repairs", "", "", "", "", "", 0, Guid.NewGuid());
        Assert.False(new HmacAiBusinessInformationFingerprint(withoutOne).Matches(business, "one",
            new HmacAiBusinessInformationFingerprint(oldRing).Create(business, "one")));
        var turn = new SubmitAiAssistantTurnRequest(Guid.NewGuid(), null, 0, 0, "What is open?", "en");
        var owner = Guid.NewGuid();
        Assert.False(new HmacAiAssistantRequestFingerprint(withoutOne).Matches(owner, turn, "one",
            new HmacAiAssistantRequestFingerprint(oldRing).Create(owner, turn, "one")));
    }
}
