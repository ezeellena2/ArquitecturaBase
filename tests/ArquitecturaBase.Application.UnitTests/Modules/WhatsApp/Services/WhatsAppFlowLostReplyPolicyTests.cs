using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// O49: a reply Meta could not deliver comes only as a failed status with 131052 («Unable to download the media sent by the user»), in the
// number or the BSUID of the person who sent it. Only a form that asks for a file can lose its reply that way, and its resend says so in the
// language of the send.
public sealed class WhatsAppFlowLostReplyPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 13, 49, 48, DateTimeKind.Utc);

    [Theory]
    [InlineData(WhatsAppMessageStatus.Failed, 131052, "5493413654813", null, true)]
    [InlineData(WhatsAppMessageStatus.Failed, 131052, null, "AR.1102953142229032", true)]
    [InlineData(WhatsAppMessageStatus.Failed, 131052, null, null, false)]
    [InlineData(WhatsAppMessageStatus.Failed, 131026, "5493413654813", null, false)]
    [InlineData(WhatsAppMessageStatus.Failed, null, "5493413654813", null, false)]
    [InlineData(WhatsAppMessageStatus.Delivered, null, "5493413654813", null, false)]
    public void Only_a_failed_status_with_131052_and_its_person_is_a_lost_reply(WhatsAppMessageStatus status, int? errorCode, string? waId,
        string? userIdentifier, bool lost) =>
        Assert.Equal(lost, WhatsAppFlowLostReplyPolicy.IsLost(new WhatsAppWebhookStatus("wamid.lost", status, Now, errorCode, waId, userIdentifier)));

    [Fact]
    public void Only_a_form_with_a_file_question_can_lose_its_reply_this_way()
    {
        var text = new WhatsAppFlowProjectionField(Guid.NewGuid(), "field_a", "shortText", false, 80, []);
        var photo = new WhatsAppFlowProjectionField(Guid.NewGuid(), "field_b", "photo", false, null, [], Files: new StructuredFormFileSettings(0, 1));

        Assert.False(WhatsAppFlowLostReplyPolicy.AsksForFile(Projection(text)));
        Assert.True(WhatsAppFlowLostReplyPolicy.AsksForFile(Projection(text, photo)));
    }

    [Theory]
    [InlineData("es", "No me llegó tu respuesta al formulario «Registrar visita». Completalo de nuevo, por favor.")]
    [InlineData("en", "Your reply to the form “Register visit” didn't reach me. Please fill it in again.")]
    public void The_resend_says_that_the_reply_did_not_arrive_in_the_language_of_the_send(string culture, string expected) =>
        Assert.Equal(expected, WhatsAppFlowLostReplyPolicy.Body(culture, """{"es":"Registrar visita","en":"Register visit"}"""));

    // A send keeps the name of its form in its language; without it, the first one it has, and without any, no resend.
    [Fact]
    public void The_resend_names_the_form_as_the_send_keeps_it()
    {
        Assert.Equal("Your reply to the form “Registrar visita” didn't reach me. Please fill it in again.",
            WhatsAppFlowLostReplyPolicy.Body("en", """{"es":"Registrar visita"}"""));
        Assert.Null(WhatsAppFlowLostReplyPolicy.Body("es", "{}"));
        Assert.Null(WhatsAppFlowLostReplyPolicy.Body("es", "not json"));
    }

    // A send created after the loss could not lose that reply, and one whose resend waits in the outgoing, or went out after the loss, already
    // gives the person a new form. A send that took something shared goes again only while it keeps what was written.
    [Fact]
    public void A_send_goes_again_only_if_it_existed_at_the_loss_and_nothing_gives_the_person_a_new_one()
    {
        Assert.True(WhatsAppFlowLostReplyPolicy.SendsAgain(Open(), Now));
        Assert.False(WhatsAppFlowLostReplyPolicy.SendsAgain(Open(createdAtUtc: Now.AddSeconds(1)), Now));
        Assert.False(WhatsAppFlowLostReplyPolicy.SendsAgain(Open() with { ResendWaiting = true }, Now));
        Assert.False(WhatsAppFlowLostReplyPolicy.SendsAgain(Open() with { LastResendAtUtc = Now }, Now));
        Assert.True(WhatsAppFlowLostReplyPolicy.SendsAgain(Open() with { LastResendAtUtc = Now.AddMinutes(-1) }, Now));
        Assert.False(WhatsAppFlowLostReplyPolicy.SendsAgain(Open(took: true), Now));
        Assert.True(WhatsAppFlowLostReplyPolicy.SendsAgain(Open(took: true, keeps: true), Now));
    }

    private static WhatsAppFlowProjection Projection(params WhatsAppFlowProjectionField[] mapping) =>
        new(1, "{}", new string('a', 64), "r14", 1, "es", mapping);

    private static WhatsAppFlowOpenSend Open(DateTime? createdAtUtc = null, bool took = false, bool keeps = false)
    {
        var session = WhatsAppFlowSession.CreateAssistant(Guid.NewGuid(), 0, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "es",
            """{"es":"Registrar visita"}""", "VIS", new string('a', 64), new string('b', 64), Now.AddDays(1));
        // The auditing sets it when the send is saved.
        typeof(WhatsAppFlowSession).GetProperty(nameof(WhatsAppFlowSession.CreatedAtUtc))!.SetValue(session, createdAtUtc ?? Now.AddMinutes(-10));
        if (took) Assert.True(session.TakeShared(WhatsAppMessageKind.Location, [Guid.NewGuid()], Now.AddMinutes(-5), Now.AddMinutes(-5)));
        if (keeps) session.SealSharedValues([1, 2, 3]);
        return new WhatsAppFlowOpenSend(session, null!, Guid.NewGuid(), []);
    }
}
