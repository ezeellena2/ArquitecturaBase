using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>
/// W3 (task 24): «Prellenado con» of a resend, with the privacy of «Por qué lo mandó», and the second response of a send with the number
/// of the one that was kept.
/// </summary>
public sealed partial class WhatsAppChatQueryActionPrivacyTests
{
    private static readonly Guid Place = Guid.CreateVersion7();
    private static readonly Guid Receiver = Guid.CreateVersion7();
    private static readonly Guid ReceiverPhone = Guid.CreateVersion7();

    [Fact]
    public async Task A_resend_shows_what_it_carried_the_location_before_the_contact_and_a_queued_one_everything_the_send_took()
    {
        var host = SharedHost();

        var thread = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);

        // The resend that went out before the contact arrived carried only the location; the queued one carries everything, in order.
        Assert.Equal([("Dirección de entrega", "Av. San Martín 1450, Rosario")],
            Assert.Single(thread.Value.Messages).Prefill!.Select(value => (value.Label, value.Value)));
        Assert.Equal([("Dirección de entrega", "Av. San Martín 1450, Rosario"), ("Recibió", "Raúl Medina"), ("Tel. de quien recibe", "+54 9 341 555-0177")],
            Assert.Single(thread.Value.Outgoing).Prefill!.Select(value => (value.Label, value.Value)));
        Assert.DoesNotContain("Raúl", Assert.Single(thread.Value.Messages).Prefill!.Single().ToString(), StringComparison.Ordinal);
    }

    // A resend that no longer waits in the queue (failed, uncertain or reviewed) says what it was queued with, not what came after it.
    [Fact]
    public async Task A_resend_that_left_the_queue_without_going_out_shows_what_the_send_had_taken_when_it_was_queued()
    {
        var host = SharedHost();
        host.Reader.Outgoing = host.Reader.Outgoing! with { State = WhatsAppOutgoingMessageState.Failed, RequestedAtUtc = Now.AddMinutes(1) };

        var thread = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);

        Assert.Equal([("Dirección de entrega", "Av. San Martín 1450, Rosario")], Assert.Single(thread.Value.Outgoing).Prefill!.Select(value => (value.Label, value.Value)));
    }

    [Fact]
    public async Task A_resend_does_not_show_its_values_without_record_read_after_the_retention_or_when_they_do_not_read()
    {
        var host = SharedHost();
        host.Source.Allowed = false;
        var restricted = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);
        Assert.Null(Assert.Single(restricted.Value.Messages).Prefill);
        Assert.Null(Assert.Single(restricted.Value.Outgoing).Prefill);

        host.Source.Allowed = true;
        host.Presentation.Protector.Shared = new(1, []);
        var unreadable = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);
        Assert.Null(Assert.Single(unreadable.Value.Messages).Prefill);

        host.Reader.Audio = host.Reader.Audio! with { SharedPrefill = null };
        var cleared = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);
        Assert.Null(Assert.Single(cleared.Value.Messages).Prefill);
    }

    [Fact]
    public async Task The_second_response_of_a_send_says_the_number_of_the_one_that_was_kept_and_no_other_reply_does()
    {
        var host = new Host();
        host.Reader.Audio = new(Guid.NewGuid(), WhatsAppMessageDirection.Inbound, WhatsAppMessageKind.FlowReply, null, Now, 9, null, null, null,
            FormReply: new("Rejected", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "{\"es\":\"Registrar entrega\"}", "es", null, null, "Forms.Record.ReplayConflict")
                { SavedNumber = "ENT-000031" });

        var rejected = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);
        Assert.Equal(("Rejected", "ENT-000031"), (Assert.Single(rejected.Value.Messages).FormReply!.State, rejected.Value.Messages[0].FormReply!.SavedNumber));

        host.Reader.Audio = host.Reader.Audio with { FormReply = host.Reader.Audio.FormReply! with { State = "Pending", RejectionCode = null } };
        var pending = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);
        Assert.Null(Assert.Single(pending.Value.Messages).FormReply!.SavedNumber);
    }

    // A send that took a location (12:00) and then a contact (12:05): its first resend went out at 12:01 and the second waits.
    private static Host SharedHost()
    {
        var host = new Host();
        var takenJson = "{\"location\":{\"atUtc\":\"2026-10-16T12:00:00Z\",\"fieldIds\":[\"" + Place + "\"]},"
            + "\"contact\":{\"atUtc\":\"2026-10-16T12:05:00Z\",\"fieldIds\":[\"" + Receiver + "\",\"" + ReceiverPhone + "\"]}}";
        var row = new WhatsAppSharedPrefillRow(Guid.NewGuid(), host.Reader.Contact.Id, Guid.NewGuid(), Guid.NewGuid(), takenJson, [3]);
        // Stored in the order it was taken; the chat reads the location before the contact.
        host.Presentation.Protector.Shared = new(1, [new(Receiver, "Recibió", "Raúl Medina"), new(ReceiverPhone, "Tel. de quien recibe", "+54 9 341 555-0177"),
            new(Place, "Dirección de entrega", "Av. San Martín 1450, Rosario")]);
        host.Reader.Audio = new(Guid.NewGuid(), WhatsAppMessageDirection.Outbound, WhatsAppMessageKind.Flow, "Puse tu ubicación en «Dirección de entrega».",
            Now.AddMinutes(1), 0, Guid.NewGuid(), WhatsAppMessageStatus.Delivered, null) { SharedPrefill = row };
        host.Reader.Outgoing = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Puse el contacto en «Recibió» y «Tel. de quien recibe».", Now.AddMinutes(5),
            WhatsAppOutgoingMessageState.Pending, null, WhatsAppOutgoingMessageKind.Flow) { SharedPrefill = row };
        return host;
    }
}
