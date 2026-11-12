using System.Text.Json;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

// W3 (task 22): what the person shares is kept once with its message, as it came: a place's address with its point, and the formatted
// name and the phones of a contact's first card. A contact without a name or without a phone is a real case.
public sealed class WhatsAppMessageSharedTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);

    private static WhatsAppMessage Inbound(WhatsAppMessageKind kind) =>
        WhatsAppMessage.Inbound(Guid.NewGuid(), "wamid." + Guid.NewGuid().ToString("N"), kind, null, null, Now);

    private static string[] Phones(WhatsAppMessage message) => JsonSerializer.Deserialize<string[]>(message.ContactPhonesJson!)!;

    [Fact]
    public void A_place_keeps_its_address_with_its_point_trimmed_and_cut_at_its_limit()
    {
        var place = Inbound(WhatsAppMessageKind.Location);
        var far = Inbound(WhatsAppMessageKind.Location);

        place.RecordLocation(-32.947102m, -60.638955m, "  Bv. Oroño 1234, S2000 Rosario, Santa Fe  ");
        far.RecordLocation(0m, 0m, new string('a', WhatsAppMessage.MaxLocationAddressLength + 1));

        Assert.Equal((-32.947102m, -60.638955m, "Bv. Oroño 1234, S2000 Rosario, Santa Fe"), (place.Latitude, place.Longitude, place.LocationAddress));
        Assert.Equal(new string('a', WhatsAppMessage.MaxLocationAddressLength), far.LocationAddress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_current_location_or_a_place_without_an_address_keeps_only_its_point(string? address)
    {
        var location = Inbound(WhatsAppMessageKind.Location);

        location.RecordLocation(-32.947102m, -60.638955m, address);

        Assert.Equal((-32.947102m, (string?)null), (location.Latitude, location.LocationAddress));
    }

    [Fact]
    public void A_contact_keeps_its_name_and_its_first_ten_phones_as_they_came()
    {
        var contact = Inbound(WhatsAppMessageKind.Contact);
        var more = Enumerable.Range(1, 12).Select(index => "+54 341 400-00" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture));

        contact.RecordContact("  Ana Gómez ", ["+54 9 341 365-4813", " +54 341 444-5566 ", .. more]);

        Assert.Equal("Ana Gómez", contact.ContactName);
        Assert.Equal(["+54 9 341 365-4813", "+54 341 444-5566", "+54 341 400-0001", "+54 341 400-0002", "+54 341 400-0003",
            "+54 341 400-0004", "+54 341 400-0005", "+54 341 400-0006", "+54 341 400-0007", "+54 341 400-0008"], Phones(contact));
    }

    // A blank phone, one longer than any number or one with a line break inside is no phone: it is left out, never cut, and does not
    // count for the ten. One exactly at the limit is still a phone.
    [Fact]
    public void A_contact_leaves_out_what_is_not_a_phone_and_cuts_its_name_at_its_limit()
    {
        var contact = Inbound(WhatsAppMessageKind.Contact);
        var longest = new string('2', WhatsAppMessage.MaxContactPhoneLength);

        contact.RecordContact(new string('n', WhatsAppMessage.MaxContactNameLength + 5),
            [null, "", "   ", new string('1', WhatsAppMessage.MaxContactPhoneLength + 1), "+54 341\n444-5566", "\0", longest, "+54 9 341 365-4813"]);

        Assert.Equal(new string('n', WhatsAppMessage.MaxContactNameLength), contact.ContactName);
        Assert.Equal([longest, "+54 9 341 365-4813"], Phones(contact));
    }

    [Fact]
    public void A_contact_without_a_name_or_without_a_phone_keeps_what_it_brings()
    {
        var withoutName = Inbound(WhatsAppMessageKind.Contact);
        var withoutPhone = Inbound(WhatsAppMessageKind.Contact);
        var blank = Inbound(WhatsAppMessageKind.Contact);

        withoutName.RecordContact(null, ["+54 9 341 365-4813"]);
        withoutPhone.RecordContact("Ana Gómez", []);
        blank.RecordContact("   ", ["  "]);

        Assert.Null(withoutName.ContactName);
        Assert.Equal(["+54 9 341 365-4813"], Phones(withoutName));
        Assert.Equal(("Ana Gómez", "[]"), (withoutPhone.ContactName, withoutPhone.ContactPhonesJson));
        // Never an empty name: what has none keeps none, and the phones are there even when there is no phone, so the card reads as kept.
        Assert.Equal(((string?)null, "[]"), (blank.ContactName, blank.ContactPhonesJson));
    }

    [Fact]
    public void Only_an_inbound_contact_records_its_card_and_only_once()
    {
        var contact = Inbound(WhatsAppMessageKind.Contact);
        contact.RecordContact("Ana Gómez", ["+54 9 341 365-4813"]);

        Assert.Throws<InvalidOperationException>(() => contact.RecordContact("Otra persona", ["+54 341 444-5566"]));
        Assert.Throws<InvalidOperationException>(() => Inbound(WhatsAppMessageKind.Other).RecordContact("Ana Gómez", []));
        Assert.Throws<InvalidOperationException>(() => WhatsAppMessage.Outbound(Guid.NewGuid(), "wamid.outbound", WhatsAppMessageKind.Contact,
            "Contacto", Now).RecordContact("Ana Gómez", []));
        Assert.Equal("Ana Gómez", contact.ContactName);
        Assert.Equal(["+54 9 341 365-4813"], Phones(contact));
    }

    [Fact]
    public void A_shared_location_or_contact_has_no_body_and_a_received_message_is_no_resend()
    {
        var location = Inbound(WhatsAppMessageKind.Location);
        location.RecordLocation(-32.947102m, -60.638955m, "Bv. Oroño 1234");
        var contact = Inbound(WhatsAppMessageKind.Contact);
        contact.RecordContact("Ana Gómez", ["+54 9 341 365-4813"]);

        Assert.All(new[] { location, contact }, message => Assert.Equal(((string?)null, false), (message.Body, message.IsResend)));
        Assert.Null(location.ContactPhonesJson);
        Assert.Null(contact.LocationAddress);
    }
}
