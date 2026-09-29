using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Channels;

/// <summary>
/// La invitación por WhatsApp, el canal del módulo: guarda el consentimiento y pone cuatro reglas, en este orden, porque
/// el front muestra el primer error (WhatsApp prendido, un número, el consentimiento y el nombre). Encola la plantilla
/// con «Quiero entrar»; si la cola no la toma, la invitación queda como no enviada.
/// </summary>
public sealed class WhatsAppInvitationChannelTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeWhatsAppSendQueue _queue = new();
    private readonly FakeLogger<WhatsAppInvitationChannel> _logger = new();

    [Fact]
    public void It_is_the_whatsapp_channel_and_records_the_consent()
    {
        var channel = Channel(whatsApp: true);

        Assert.Equal(UserInvitationChannel.WhatsApp, channel.Channel);
        Assert.True(channel.RecordsConsent);
    }

    [Fact]
    public void With_whatsapp_off_it_is_unavailable_on_the_channel_before_any_other_rule()
    {
        var result = Channel(whatsApp: false).Check(Check(hasPhone: false, consent: false, displayName: null));

        AssertOnField(result, "invitation.channel", ValidationMessages.InvitationWhatsAppUnavailable);
    }

    [Fact]
    public void Without_a_phone_it_asks_for_one_on_the_channel_before_the_consent()
    {
        var result = Channel(whatsApp: true).Check(Check(hasPhone: false, consent: false, displayName: null));

        AssertOnField(result, "invitation.channel", ValidationMessages.InvitationPhoneRequired);
    }

    [Fact]
    public void Without_the_consent_it_asks_for_it_on_the_consent_before_the_name()
    {
        var result = Channel(whatsApp: true).Check(Check(hasPhone: true, consent: false, displayName: null));

        Assert.Equal(UserInvitationErrors.ConsentRequiredCode, result.Error.Code);
        Assert.Equal(["invitation.consent"], Assert.IsType<ValidationError>(result.Error).Errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Without_a_name_it_asks_for_it_on_the_name(string? displayName)
    {
        var result = Channel(whatsApp: true).Check(Check(hasPhone: true, consent: true, displayName));

        Assert.Equal(UserInvitationErrors.NameRequiredCode, result.Error.Code);
        Assert.Equal(["displayName"], Assert.IsType<ValidationError>(result.Error).Errors.Keys);
    }

    [Fact]
    public void With_everything_it_does_not_ask_for_an_email()
    {
        Assert.True(Channel(whatsApp: true).Check(Check(hasPhone: true, consent: true, "Ana")).IsSuccess);
    }

    [Fact]
    public void It_queues_the_template_with_the_button_to_enter_in_the_culture_of_the_account()
    {
        var user = Account();
        var invitation = Invitation(user);

        Channel(whatsApp: true).Enqueue(user, invitation, "en");

        var message = Assert.IsType<WhatsAppInvitationMessage>(Assert.Single(_queue.Messages));
        Assert.Equal("+5493515550101", message.To.Value);
        Assert.Equal(user.Id, message.UserId);
        Assert.Equal(invitation.Id, message.InvitationId);
        Assert.Equal("en", message.LanguageCode);
        Assert.Equal("Ana", message.Name);
        Assert.Equal("Test", message.AppName);
        Assert.Equal(BotButtons.WantToEnter, message.ReplyPayload);
        Assert.False(invitation.SendFailed);
        Assert.Empty(_logger.Collector.GetSnapshot());
    }

    [Fact]
    public void A_full_queue_marks_the_invitation_as_not_sent_and_logs_it_without_personal_data()
    {
        _queue.Accepts = false;
        var user = Account();
        var invitation = Invitation(user);

        Channel(whatsApp: true).Enqueue(user, invitation, "es");

        Assert.True(invitation.SendFailed);
        Assert.Empty(_queue.Messages);
        var log = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.DoesNotContain("5550101", log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Ana", log.Message, StringComparison.Ordinal);
    }

    private WhatsAppInvitationChannel Channel(bool whatsApp) =>
        new(_queue, new FakeWhatsAppAvailability(whatsApp), new FakeAppName("Test"), _logger);

    private static InvitationCheck Check(bool hasPhone, bool consent, string? displayName) => new(
        HasEmail: false,
        hasPhone,
        consent,
        displayName,
        "invitation.channel",
        "invitation.consent",
        "displayName");

    private static void AssertOnField(Result result, string field, string message)
    {
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal([field], error.Errors.Keys);
        Assert.Equal([message], error.Errors[field]);
    }

    private static UserAccount Account() => new(
        Guid.CreateVersion7(),
        Email: null,
        EmailConfirmed: false,
        "+5493515550101",
        PhoneNumberConfirmed: false,
        "Ana",
        "es",
        "UTC",
        IsActive: true);

    private static UserInvitation Invitation(UserAccount user) =>
        UserInvitation.Send(user.Id, UserInvitationChannel.WhatsApp, Guid.CreateVersion7(), Now, consentConfirmed: true);
}
