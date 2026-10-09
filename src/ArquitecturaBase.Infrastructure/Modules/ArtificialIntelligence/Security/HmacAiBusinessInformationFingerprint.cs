using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;
internal sealed class HmacAiBusinessInformationFingerprint(AiKeyRing ring) : IAiBusinessInformationFingerprint
{
    public string ActiveKeyId => ring.ActiveKeyId;
    public string Create(UpdateAiBusinessInformationRequest request, string keyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = ring.BusinessInformationFingerprintKey(keyId);
        List<object> values = [request.Name.Trim(), request.Description.Trim(), request.Hours.Trim(),
            request.Location.Trim(), request.Services.Trim(), request.Policies.Trim(), request.Faq.Trim(), request.ExpectedRevision, request.RequestId];
        // Omitted details preserve the exact v1 receipt payload during an additive deployment.
        if (request.Details is { } details)
        {
            var normalized = details.Normalize().Value;
            var location = normalized.Location;
            values.Add("details:v1");
            // Keep the deployed property order and null coordinates for historical receipts.
            values.Add(new { Location = new { location.Mode, location.Address, location.Reference, location.Area,
                location.Latitude, location.Longitude, location.Link }, normalized.OpeningHours, normalized.Faq });
            if (location.PlaceId is { } placeId) { values.Add("place:v1"); values.Add(placeId); }
        }
        var payload = JsonSerializer.SerializeToUtf8Bytes(values);
        try { return Convert.ToBase64String(HMACSHA256.HashData(key, payload)); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(payload); }
    }
    public bool Matches(UpdateAiBusinessInformationRequest request, string keyId, string fingerprint)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(Create(request, keyId)), Convert.FromBase64String(fingerprint)); }
        catch (FormatException) { return false; }
    }
}
