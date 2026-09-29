using System.Globalization;
using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Channels;

/// <summary>
/// La invitación por correo, el canal que trae el núcleo: pide que la cuenta tenga correo, no guarda consentimiento y
/// encola el correo con el botón a /login. Si la cola no lo toma, la invitación queda como no enviada y un aviso sin
/// datos personales.
/// </summary>
public sealed class EmailInvitationChannelTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeEmailQueue _queue = new();
    private readonly FakeEmailTemplateRenderer _templates = new();
    private readonly FakeLogger<EmailInvitationChannel> _logger = new();

    [Fact]
    public void It_is_the_email_channel_and_records_no_consent()
    {
        var channel = Channel();

        Assert.Equal(UserInvitationChannel.Email, channel.Channel);
        Assert.False(channel.RecordsConsent);
    }

    [Fact]
    public void Without_an_email_the_invitation_is_rejected_on_the_channel_field()
    {
        var result = Channel().Check(Check(hasEmail: false));

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal([ValidationMessages.InvitationEmailRequired], error.Errors["invitation.channel"]);
        Assert.Single(error.Errors);
    }

    [Fact]
    public void With_an_email_it_asks_for_nothing_else()
    {
        // Ni teléfono, ni consentimiento, ni nombre: el correo saluda sin nombre si no hay.
        Assert.True(Channel().Check(Check(hasEmail: true)).IsSuccess);
    }

    [Fact]
    public void It_queues_the_email_with_the_login_button_in_the_culture_of_the_account()
    {
        var user = Account(culture: "en");
        var invitation = UserInvitation.ByEmail(user.Id, Guid.CreateVersion7(), Now);

        Channel().Enqueue(user, invitation, "en");

        var message = Assert.Single(_queue.Messages);
        Assert.Equal("ana@example.test", message.To);
        Assert.Equal(CultureInfo.GetCultureInfo("en"), _templates.LastCulture);
        Assert.Equal("https://example.test/login", _templates.LastLoginUrl);
        Assert.False(invitation.SendFailed);
        Assert.Empty(_logger.Collector.GetSnapshot());
    }

    [Fact]
    public void A_full_queue_marks_the_invitation_as_not_sent_and_logs_it_without_personal_data()
    {
        _queue.Accepts = false;
        var user = Account(culture: "es");
        var invitation = UserInvitation.ByEmail(user.Id, Guid.CreateVersion7(), Now);

        Channel().Enqueue(user, invitation, "es");

        Assert.True(invitation.SendFailed);
        Assert.Empty(_queue.Messages);
        var log = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.DoesNotContain("ana@example.test", log.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ana", log.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_public_origin_it_cannot_build_the_login_button()
    {
        var channel = new EmailInvitationChannel(_queue, _templates, new FakePublicOrigin(null), _logger);
        var user = Account(culture: "es");

        Assert.Throws<InvalidOperationException>(() =>
            channel.Enqueue(user, UserInvitation.ByEmail(user.Id, Guid.CreateVersion7(), Now), "es"));
        Assert.Empty(_queue.Messages);
    }

    private EmailInvitationChannel Channel() =>
        new(_queue, _templates, new FakePublicOrigin(new Uri("https://example.test/")), _logger);

    private static InvitationCheck Check(bool hasEmail) => new(
        hasEmail,
        HasPhone: false,
        Consent: false,
        DisplayName: null,
        "invitation.channel",
        "invitation.consent",
        "displayName");

    private static UserAccount Account(string culture) => new(
        Guid.CreateVersion7(),
        "ana@example.test",
        EmailConfirmed: true,
        PhoneNumber: null,
        PhoneNumberConfirmed: false,
        "Ana",
        culture,
        "UTC",
        IsActive: true);
}
