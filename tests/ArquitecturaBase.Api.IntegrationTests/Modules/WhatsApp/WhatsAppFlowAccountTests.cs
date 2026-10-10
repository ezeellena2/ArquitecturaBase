using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppFlowAccountTests
{
    [Theory]
    [InlineData(null, "v25.0", false)]
    [InlineData("", "v25.0", false)]
    [InlineData("wrong-id", "v25.0", false)]
    [InlineData("222", "v26.0", false)]
    [InlineData("222", "v25.0", true)]
    public void Publication_capacity_requires_its_account_and_frozen_profile_without_disabling_text(string? wabaId, string graph, bool expected)
    {
        var options = new WhatsAppOptions { PhoneNumberId = "111", AccessToken = "test-token", WabaId = wabaId, GraphApiVersion = graph };
        var environment = new WhatsAppFlowEnvironment(Options.Create(options), null!);
        Assert.Equal(expected, environment.IsConfigured);
        Assert.Equal("111", options.PhoneNumberId);
        Assert.Equal("test-token", options.AccessToken);
    }

    [Fact]
    public void Token_rotation_preserves_flow_identity_and_old_sealed_fingerprints_remain_usable()
    {
        var oldSettings = new WhatsAppOptions { PhoneNumberId = "111", WabaId = "222", AccessToken = "old-token" };
        var newSettings = new WhatsAppOptions { PhoneNumberId = "111", WabaId = "222", AccessToken = "new-token" };
        var oldLegacyFingerprint = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("222\n111\nv25.0\nold-token")));
        var sealedAccount = new WhatsAppFlowAccountContext("222", "111", "v25.0", oldLegacyFingerprint);
        var currentAccount = new WhatsAppFlowAccountContext("222", "111", "v25.0", WhatsAppFlowEnvironment.Fingerprint(newSettings));

        Assert.Equal(WhatsAppFlowEnvironment.Fingerprint(oldSettings), WhatsAppFlowEnvironment.Fingerprint(newSettings));
        Assert.True(currentAccount.HasSameIdentity(sealedAccount));
        Assert.False(currentAccount.HasSameIdentity(new("999", "111", "v25.0", currentAccount.ConfigurationFingerprint)));
    }
}
