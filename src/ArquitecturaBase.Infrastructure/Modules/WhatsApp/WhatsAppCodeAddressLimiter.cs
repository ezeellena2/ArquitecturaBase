using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>
/// The daily limit per address of the codes while registration is open (H10): a fixed window of a day per address, in the memory of
/// this copy, like the limits of the HTTP edge. A place is taken only by a code that went out, so the addresses it keeps are bounded
/// by the daily limit of everyone; an IPv6 address counts by its /64, so rotating within it does not escape. A refusal answers the
/// length of the window: the fixed window does not say how much of it is left.
/// </summary>
internal sealed class WhatsAppCodeAddressLimiter : IWhatsAppCodeAddressLimiter, IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public WhatsAppCodeAddressLimiter(IOptions<WhatsAppLoginOptions> options)
    {
        var permits = options.Value.DailySignInCodeLimitPerAddress;
        _limiter = PartitionedRateLimiter.Create<string, string>(address => RateLimitPartition.GetFixedWindowLimiter(address,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromDays(1), QueueLimit = 0, AutoReplenishment = true }),
            StringComparer.Ordinal);
    }

    public TimeSpan? TryTake(string address)
    {
        using var lease = _limiter.AttemptAcquire(Key(address));
        if (lease.IsAcquired) return null;
        return lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : TimeSpan.FromDays(1);
    }

    public void Dispose() => _limiter.Dispose();

    /// <summary>An IPv4 as it is (also mapped into IPv6) and an IPv6 by its /64; something that is no address, as it came.</summary>
    internal static string Key(string address)
    {
        if (!IPAddress.TryParse(address, out var ip)) return address;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
