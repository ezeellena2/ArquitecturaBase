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
        Assert.Null(invitation.WaMessageId);
        Assert.False(invitation.SendFailed);
    }

    [Fact]
    public void An_invitation_by_whatsapp_records_that_the_sender_confirmed_the_consent_and_when()
    {
        var invitation = UserInvitation.ByWhatsApp(Invited, Admin, Now);

        Assert.Equal(UserInvitationChannel.WhatsApp, invitation.Channel);
        Assert.Equal(Admin, invitation.SentBy);
        Assert.Equal(Admin, invitation.ConsentConfirmedBy);
        Assert.Equal(Now, invitation.ConsentConfirmedAtUtc);
    }

    [Fact]
    public void An_invitation_needs_the_account_and_who_sends_it()
    {
        Assert.Throws<ArgumentException>(() => UserInvitation.ByEmail(Guid.Empty, Admin, Now));
        Assert.Throws<ArgumentException>(() => UserInvitation.ByWhatsApp(Invited, Guid.Empty, Now));
    }

    [Fact]
    public void The_whatsapp_message_keeps_the_first_meta_id_it_gets()
    {
        var invitation = UserInvitation.ByWhatsApp(Invited, Admin, Now);

        invitation.AttachWhatsAppMessage(WaMessageId);
        invitation.AttachWhatsAppMessage("wamid.other");

        Assert.Equal(WaMessageId, invitation.WaMessageId);
    }

    [Fact]
    public void Only_an_invitation_by_whatsapp_has_a_whatsapp_message()
    {
        var invitation = UserInvitation.ByEmail(Invited, Admin, Now);

        Assert.Throws<InvalidOperationException>(() => invitation.AttachWhatsAppMessage(WaMessageId));
        Assert.Throws<ArgumentException>(() => UserInvitation.ByWhatsApp(Invited, Admin, Now).AttachWhatsAppMessage(" "));
    }

    [Fact]
    public void An_invitation_that_could_not_be_sent_is_marked_as_failed()
    {
        var invitation = UserInvitation.ByWhatsApp(Invited, Admin, Now);

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
        var invitation = UserInvitation.ByWhatsApp(Invited, Admin, Now);
        invitation.MarkSendFailed();

        Assert.Equal(TimeSpan.Zero, invitation.WaitBeforeAnother(Now.AddSeconds(1)));
    }
}
