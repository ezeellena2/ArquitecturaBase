using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;

// I7: the access guard probes the saved key locally before offering or accepting anything that needs it. Every saved key of the
// fixtures reads as a fictional one, except `UnreadableKey`, which stands for a key the installation can no longer decrypt.
internal static class FictionalCredentials
{
    public const string UnreadableKey = "fictional-unreadable-key";

    public static AiConnectionCandidatePolicy Policy => new(new Protector());

    // Counts how many times the saved key is probed, so a test sees the in-memory checks go first and one probe per request (I7, review).
    internal sealed class Protector : IAiSecretProtector
    {
        public int Probes { get; private set; }
        public string Protect(AiProvider provider, string apiKey) => "protected:" + apiKey;
        public string? Unprotect(AiProvider provider, string protectedApiKey) => protectedApiKey == UnreadableKey ? null : "fictional-key";
        public bool CanUnprotect(AiProvider provider, string protectedApiKey)
        {
            Probes++;
            return Unprotect(provider, protectedApiKey) is not null;
        }
    }
}
