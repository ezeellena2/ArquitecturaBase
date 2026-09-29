using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Domain.UnitTests.Users;

public sealed class UserInvitationTests
{
    private const string WaMessageId = "wamid.HBgNNTQ5MzQxMzY1NDgxMxUCABEYEjBBQTQ1";

    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Invited = Guid.CreateVersion7();
    private static readonly Guid Admin = Guid.CreateVersion7();

    [Fact]
    public void An_invitation_by_email_records_who_sent_it_and_when_without_consent()
    {
        var invitation = UserInvitation.ByEmail(Invited, Admin, Now);

        Assert.Equal(Invited, invitation.UserId);
        Assert.Equal(UserInvitationChannel.Email, invitation.Channel);
        Assert.Equal(Admin, invitation.SentBy);
        Assert.Equal(Now, invitation.SentAtUtc);
        Assert.Null(invitation.ConsentConfirmedBy);
        Assert.Null(invitation.ConsentConfirmedAtUtc);
        Assert.Null(invitation.ProviderMessageId);
        Assert.False(invitation.SendFailed);
    }

    [Fact]
    public void Sending_with_the_consent_confirmed_records_that_the_sender_confirmed_it_and_when()
    {
        var invitation = UserInvitation.Send(Invited, UserInvitationChannel.WhatsApp, Admin, Now, consentConfirmed: true);

        Assert.Equal(Invited, invitation.UserId);
        Assert.Equal(UserInvitationChannel.WhatsApp, invitation.Channel);
        Assert.Equal(Admin, invitation.SentBy);
        Assert.Equal(Now, invitation.SentAtUtc);
        Assert.Equal(Admin, invitation.ConsentConfirmedBy);
        Assert.Equal(Now, invitation.ConsentConfirmedAtUtc);
        Assert.Null(invitation.ProviderMessageId);
        Assert.False(invitation.SendFailed);
    }

    [Fact]
    public void Sending_by_email_is_a_send_without_consent()
    {
        var sent = UserInvitation.Send(Invited, UserInvitationChannel.Email, Admin, Now, consentConfirmed: false);
        var byEmail = UserInvitation.ByEmail(Invited, Admin, Now);

        Assert.Equal(
            (sent.UserId, sent.Channel, sent.SentBy, sent.SentAtUtc, sent.ConsentConfirmedBy, sent.ConsentConfirmedAtUtc),
            (byEmail.UserId, byEmail.Channel, byEmail.SentBy, byEmail.SentAtUtc, byEmail.ConsentConfirmedBy, byEmail.ConsentConfirmedAtUtc));
    }

    [Fact]
    public void An_invitation_needs_the_account_and_who_sends_it()
    {
        Assert.Throws<ArgumentException>(() => UserInvitation.ByEmail(Guid.Empty, Admin, Now));
        Assert.Throws<ArgumentException>(() =>
            UserInvitation.Send(Invited, UserInvitationChannel.WhatsApp, Guid.Empty, Now, consentConfirmed: true));
    }

    [Fact]
    public void The_provider_message_keeps_the_first_id_it_gets()
    {
        var invitation = ByWhatsApp();

        invitation.AttachProviderMessage(WaMessageId);
        invitation.AttachProviderMessage("wamid.other");

        Assert.Equal(WaMessageId, invitation.ProviderMessageId);
    }

    [Fact]
    public void An_invitation_by_email_has_no_provider_message()
    {
        var invitation = UserInvitation.ByEmail(Invited, Admin, Now);

        Assert.Throws<InvalidOperationException>(() => invitation.AttachProviderMessage(WaMessageId));
        Assert.Null(invitation.ProviderMessageId);
    }

    [Fact]
    public void The_provider_message_id_fits_its_column()
    {
        // 256, el largo que ya tenía el id de Meta: la columna no cambia.
        Assert.Equal(256, UserInvitation.MaxProviderMessageIdLength);
        Assert.Throws<ArgumentException>(() => ByWhatsApp().AttachProviderMessage(" "));
        Assert.Throws<ArgumentException>(() =>
            ByWhatsApp().AttachProviderMessage(new string('w', UserInvitation.MaxProviderMessageIdLength + 1)));

        var longest = new string('w', UserInvitation.MaxProviderMessageIdLength);
        var invitation = ByWhatsApp();
        invitation.AttachProviderMessage(longest);

        Assert.Equal(longest, invitation.ProviderMessageId);
    }

    [Fact]
    public void An_invitation_that_could_not_be_sent_is_marked_as_failed()
    {
        var invitation = ByWhatsApp();

        invitation.MarkSendFailed();

        Assert.True(invitation.SendFailed);
    }

    [Fact]
    public void Another_invitation_to_the_same_account_waits_a_minute_after_one_that_was_sent()
    {
        var invitation = UserInvitation.ByEmail(Invited, Admin, Now);

        Assert.Equal(TimeSpan.FromMinutes(1), UserInvitation.ResendCooldown);
        Assert.Equal(TimeSpan.FromSeconds(60), invitation.WaitBeforeAnother(Now));
        Assert.Equal(TimeSpan.FromSeconds(1), invitation.WaitBeforeAnother(Now.AddSeconds(59)));
        Assert.Equal(TimeSpan.Zero, invitation.WaitBeforeAnother(Now.AddSeconds(60)));
        Assert.Equal(TimeSpan.Zero, invitation.WaitBeforeAnother(Now.AddHours(1)));
    }

    [Fact]
    public void An_invitation_that_failed_does_not_make_the_next_one_wait()
    {
        // No le llegó nada a la persona: la espera la protege de recibir dos seguidas, y acá no hay dos.
        var invitation = ByWhatsApp();
        invitation.MarkSendFailed();

        Assert.Equal(TimeSpan.Zero, invitation.WaitBeforeAnother(Now.AddSeconds(1)));
    }

    private static UserInvitation ByWhatsApp() =>
        UserInvitation.Send(Invited, UserInvitationChannel.WhatsApp, Admin, Now, consentConfirmed: true);
}
