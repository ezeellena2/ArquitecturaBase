using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppInboundPolicyTests
{
    [Theory]
    [InlineData(WhatsAppMessageKind.Text, "WANT_TO_ENTER")]
    [InlineData(WhatsAppMessageKind.Text, null)]
    [InlineData(WhatsAppMessageKind.Media, null)]
    [InlineData(WhatsAppMessageKind.Other, null)]
    [InlineData(WhatsAppMessageKind.ButtonReply, "UNKNOWN")]
    public void Business_messages_are_not_classified_from_text_or_unknown_payloads(WhatsAppMessageKind kind, string? id)
    {
        Assert.Equal((WhatsAppMessagePurpose.Business, (WhatsAppAccessEventKind?)null), WhatsAppInboundPolicy.Classify(kind, id));
    }

    [Theory]
    [InlineData("WANT_TO_ENTER")]
    [InlineData("HAVE_ACCOUNT")]
    [InlineData("CREATE_ACCOUNT")]
    [InlineData("DID_NOT_REQUEST_CODE")]
    public void Only_known_structured_buttons_are_access(string id)
    {
        Assert.Equal((WhatsAppMessagePurpose.Access, (WhatsAppAccessEventKind?)WhatsAppAccessEventKind.AccessRequested),
            WhatsAppInboundPolicy.Classify(WhatsAppMessageKind.ButtonReply, id));
    }

    [Fact]
    public void System_notices_have_no_demonstrable_business_or_access_purpose() =>
        Assert.Equal((WhatsAppMessagePurpose.Unclassified, (WhatsAppAccessEventKind?)null),
            WhatsAppInboundPolicy.Classify(WhatsAppMessageKind.System, "WANT_TO_ENTER"));
}
