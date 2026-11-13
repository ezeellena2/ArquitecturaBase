using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// The activity a channel leaves before the response: W1-A (the location asked before the form) and W3 (task 24: what the person
// shared while the form was open, with the questions it completed by the labels of the version, and each time the form went out again).
public sealed class FormResponseHistoryPolicyTests
{
    private static readonly DateTime Sent = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Client = Guid.CreateVersion7();
    private static readonly Guid Address = Guid.CreateVersion7();
    private static readonly Guid Receiver = Guid.CreateVersion7();
    private static readonly Guid Phone = Guid.CreateVersion7();
    private static readonly Guid Operator = Guid.CreateVersion7();

    [Fact]
    public void A_send_without_a_location_or_anything_shared_says_only_that_the_form_was_sent()
    {
        var history = FormResponseHistoryPolicy.Channel(Origin(), Schema(), "es");

        var sent = Assert.Single(history);
        Assert.Equal(("Sent", Sent, Operator, "Ana Pérez"), (sent.Kind, sent.AtUtc, sent.Actor!.UserId, sent.Actor.DisplayName));
        Assert.Null(sent.Questions);
    }

    [Fact]
    public void What_was_shared_says_the_questions_it_completed_in_the_language_of_the_version_and_each_resend_its_sender()
    {
        var origin = Origin() with
        {
            SharedLocation = new(Sent.AddMinutes(3), [Address]), SharedContact = new(Sent.AddMinutes(1), [Receiver, Phone]),
            ResentAtUtc = [Sent.AddMinutes(3), Sent.AddMinutes(1)],
        };

        var history = FormResponseHistoryPolicy.Channel(origin, Schema(), "en");

        Assert.Equal(["Sent", "ContactReceived", "SentAgain", "LocationReceived", "SentAgain"], history.Select(item => item.Kind));
        Assert.Equal([Sent, Sent.AddMinutes(1), Sent.AddMinutes(1), Sent.AddMinutes(3), Sent.AddMinutes(3)], history.Select(item => item.AtUtc));
        Assert.Equal(["Received by", "Recipient phone"], history[1].Questions);
        Assert.Equal(["Delivery address"], history[3].Questions);
        Assert.All(history.Where(item => item.Kind == "SentAgain"), item => Assert.Equal(Operator, item.Actor!.UserId));
        Assert.All(history.Where(item => item.Kind is "LocationReceived" or "ContactReceived"), item => Assert.Null(item.Actor));
    }

    [Fact]
    public void The_location_asked_before_the_form_keeps_its_rows_without_questions_and_a_contact_with_only_its_phone_names_one()
    {
        var origin = Origin() with
        {
            SenderUserId = null, SenderName = null, LocationRequestedAtUtc = Sent.AddMinutes(-5), LocationResolvedAtUtc = Sent.AddMinutes(-1),
            SharedContact = new(Sent.AddMinutes(2), [Phone]), ResentAtUtc = [Sent.AddMinutes(2)],
        };

        var history = FormResponseHistoryPolicy.Channel(origin, Schema(), "es");

        Assert.Equal(["LocationRequested", "LocationReceived", "Sent", "ContactReceived", "SentAgain"], history.Select(item => item.Kind));
        Assert.Null(history[1].Questions);
        Assert.Equal(["Tel. de quien recibe"], history[3].Questions);
        Assert.All(history, item => Assert.Null(item.Actor));
    }

    // What a send took keeps the instant of the clock (100 ns) and its resend the database's (1 µs): read at the database's precision,
    // the share goes before the resend it caused.
    [Fact]
    public void A_share_and_its_resend_of_the_same_moment_keep_their_order_at_the_precision_of_the_database()
    {
        var moment = Sent.AddMinutes(1);
        var origin = Origin() with { SharedLocation = new(moment.AddTicks(7), [Address]), ResentAtUtc = [moment] };

        var history = FormResponseHistoryPolicy.Channel(origin, Schema(), "es");

        Assert.Equal(["Sent", "LocationReceived", "SentAgain"], history.Select(item => item.Kind));
        Assert.Equal([Sent, moment, moment], history.Select(item => item.AtUtc));
    }

    [Fact]
    public void What_names_a_question_the_version_does_not_have_is_left_out_never_guessed()
    {
        var origin = Origin() with { SharedLocation = new(Sent.AddMinutes(1), [Guid.CreateVersion7()]), SharedContact = new(Sent.AddMinutes(2), [Receiver, Guid.CreateVersion7()]) };

        var history = FormResponseHistoryPolicy.Channel(origin, Schema(), "es");

        Assert.Equal(["Sent", "ContactReceived"], history.Select(item => item.Kind));
        Assert.Equal(["Recibió"], history[1].Questions);
    }

    private static StructuredRecordOrigin Origin() => new(Guid.CreateVersion7(), "phone", Operator, "Ana Pérez", Sent, null, "+54 9 341 ***-**77", "Manual");

    private static FormVersionSchema Schema() => new(1,
    [
        Field(Client, "Cliente", "Client"), Field(Address, "Dirección de entrega", "Delivery address"), Field(Receiver, "Recibió", "Received by"),
        Field(Phone, "Tel. de quien recibe", "Recipient phone"),
    ]);

    private static CanonicalFormField Field(Guid id, string es, string en) =>
        new(id, "field_" + id.ToString("N"), "shortText", new Dictionary<string, string> { ["es"] = es, ["en"] = en }, null, false, 60, null);
}
