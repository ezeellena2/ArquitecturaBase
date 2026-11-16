using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Delivery;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Channels;

/// <summary>
/// N5: the state of an invitation by email comes from the queue. Without the Message-ID it is still waiting in the queue while its
/// row can live, and after that nobody knows; with it, the mail server accepted it. It is never «sent» by default.
/// </summary>
public sealed class EmailInvitationDeliveryStatusSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime SentAtUtc = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task An_invitation_by_email_without_its_message_id_is_pending_and_with_it_sent()
    {
        var source = new EmailInvitationDeliveryStatusSource(new FakeTimeProvider(SentAtUtc));

        Assert.Equal(UserInvitationChannel.Email, source.Channel);
        Assert.Equal(InvitationDeliveryStatus.Pending, await source.FindStatusAsync(null, SentAtUtc, Ct));
        Assert.Equal(InvitationDeliveryStatus.Sent, await source.FindStatusAsync("<abc@example.test>", SentAtUtc, Ct));
    }

    // Pending only while its row can still be in the queue; after that a claim abandoned mid-send may have gone out.
    [Fact]
    public async Task An_invitation_by_email_without_news_once_its_queue_row_expired_is_uncertain()
    {
        var clock = new FakeTimeProvider(SentAtUtc);
        var source = new EmailInvitationDeliveryStatusSource(clock);

        clock.Advance(QueuedDelivery.DefaultLifetime - TimeSpan.FromSeconds(1));
        Assert.Equal(InvitationDeliveryStatus.Pending, await source.FindStatusAsync(null, SentAtUtc, Ct));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(InvitationDeliveryStatus.Uncertain, await source.FindStatusAsync(null, SentAtUtc, Ct));
        Assert.Equal(InvitationDeliveryStatus.Sent, await source.FindStatusAsync("<abc@example.test>", SentAtUtc, Ct));
    }
}
