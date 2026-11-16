using System.Net;
using ArquitecturaBase.Application.Common.RateLimiting;

namespace ArquitecturaBase.Application.UnitTests.Common.RateLimiting;

// N20: the one partition per address of the limits (the HTTP ones per IP and the daily one of the WhatsApp codes). An
// IPv6 counts by its /64, so rotating inside the network a provider hands out does not escape.
public sealed class AddressPartitionTests
{
    [Theory]
    [InlineData("203.0.113.10", "203.0.113.10")]
    [InlineData("::ffff:203.0.113.10", "203.0.113.10")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:10", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::ffff", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:3::10", "2001:db8:1:3::/64")]
    [InlineData("::1", "::/64")]
    public void An_ipv4_counts_as_it_is_and_an_ipv6_by_its_network_of_64(string address, string expected)
    {
        Assert.Equal(expected, AddressPartition.Of(IPAddress.Parse(address)));
        Assert.Equal(expected, AddressPartition.Of(address));
    }

    [Fact]
    public void Two_addresses_of_the_same_64_share_the_partition_and_another_network_does_not()
    {
        Assert.Equal(AddressPartition.Of("2001:db8:1:2::10"), AddressPartition.Of("2001:db8:1:2:ffff::1"));
        Assert.NotEqual(AddressPartition.Of("2001:db8:1:2::10"), AddressPartition.Of("2001:db8:1:3::10"));
        Assert.Equal(AddressPartition.Of("::ffff:203.0.113.10"), AddressPartition.Of("203.0.113.10"));
    }

    [Fact]
    public void Without_an_address_every_request_shares_the_unknown_partition()
    {
        Assert.Equal(AddressPartition.Unknown, AddressPartition.Of((IPAddress?)null));
        Assert.Equal(AddressPartition.Unknown, AddressPartition.Of((string?)null));
        Assert.Equal(AddressPartition.Unknown, AddressPartition.Of(string.Empty));
    }

    [Fact]
    public void Something_that_is_not_an_address_counts_as_it_came()
    {
        Assert.Equal("not-an-address", AddressPartition.Of("not-an-address"));
    }

    // N7: what a code by email keeps of the address that requested it. Without a known address there is nothing to keep,
    // and it does not share a place with anyone.
    [Theory]
    [InlineData("2001:db8:1:2:aaaa::10", "2001:db8:1:2::/64")]
    [InlineData("::ffff:203.0.113.10", "203.0.113.10")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("not-an-address", null)]
    public void A_known_address_is_kept_by_its_partition_and_an_unknown_one_is_null(string? address, string? expected)
    {
        Assert.Equal(expected, AddressPartition.OfKnown(address));
    }
}
