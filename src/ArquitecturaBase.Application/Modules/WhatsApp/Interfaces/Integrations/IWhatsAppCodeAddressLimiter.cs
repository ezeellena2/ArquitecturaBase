namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

/// <summary>
/// The daily limit per address of the WhatsApp codes that go out while registration is open (H10). It lives in the memory of each copy,
/// like the other limits per address of the HTTP edge (H26): a restart starts it over, and with several copies each one counts its own.
/// An IPv6 address counts by its /64, the network of one client.
/// </summary>
public interface IWhatsAppCodeAddressLimiter
{
    /// <summary>Takes a place for <paramref name="address"/>: null when it had one, or else the length of the window (a day) as the wait.</summary>
    TimeSpan? TryTake(string address);
}
