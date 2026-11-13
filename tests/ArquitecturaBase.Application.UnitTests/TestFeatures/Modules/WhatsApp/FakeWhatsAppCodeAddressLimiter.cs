using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

/// <summary>The limit per address of the sign-in codes (H10): it records each address it is asked for and answers what it is told.</summary>
internal sealed class FakeWhatsAppCodeAddressLimiter : IWhatsAppCodeAddressLimiter
{
    public TimeSpan? RetryAfter { get; set; }

    public List<string> Taken { get; } = [];

    public TimeSpan? TryTake(string address)
    {
        Taken.Add(address);
        return RetryAfter;
    }
}
