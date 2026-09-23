using ArquitecturaBase.Domain.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.WhatsApp;

public sealed class WhatsAppContactTests
{
    private const string WaId = "5493413654813";
    private const string Bsuid = "AR.1102953142229032";

    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_contact_keeps_the_bsuid_the_wa_id_and_the_profile_name()
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, "Ana Pérez", Now);

        Assert.Equal(WaId, contact.WaId);
        Assert.Equal(Bsuid, contact.UserIdentifier);
        Assert.Equal("Ana Pérez", contact.ProfileName);
        Assert.Null(contact.UserId);
        Assert.Equal(Now, contact.LastInboundAtUtc);
    }

    /// <summary>Cuando WhatsApp empiece a ocultar números, el contacto llega solo con el BSUID; hoy puede llegar sin él.</summary>
    [Fact]
    public void A_contact_needs_the_bsuid_or_the_wa_id_but_not_both()
    {
        var onlyBsuid = WhatsAppContact.Create(waId: "", Bsuid, profileName: null, Now);
        var onlyWaId = WhatsAppContact.Create(WaId, userIdentifier: null, profileName: " ", Now);

        Assert.Null(onlyBsuid.WaId);
        Assert.Null(onlyWaId.UserIdentifier);
        Assert.Null(onlyWaId.ProfileName);
        Assert.Throws<ArgumentException>(() => WhatsAppContact.Create(waId: null, userIdentifier: " ", profileName: "Ana", Now));
    }

    [Theory]
    [InlineData("+5493413654813")]
    [InlineData("549 341 365")]
    [InlineData("12345678901234567890123456789012345")]
    public void A_wa_id_is_only_digits_as_whatsapp_sends_it(string waId)
    {
        Assert.False(WhatsAppContact.IsValidWaId(waId));
        Assert.Throws<ArgumentException>(() => WhatsAppContact.Create(waId, Bsuid, null, Now));
    }

    /// <summary>Meta documenta los BSUID con "hasta 256 caracteres alfanuméricos": 256 entra y 257 no.</summary>
    [Fact]
    public void A_bsuid_with_spaces_or_too_long_is_not_valid()
    {
        Assert.True(WhatsAppContact.IsValidUserIdentifier("user.9373795779eb6441c8adb2eaee5b848e7dd174ddd302d7db62142f4722d574b6"));
        Assert.True(WhatsAppContact.IsValidUserIdentifier(new string('a', 256)));
        Assert.False(WhatsAppContact.IsValidUserIdentifier("AR 1102953142229032"));
        Assert.False(WhatsAppContact.IsValidUserIdentifier(new string('a', 257)));
        Assert.Throws<ArgumentException>(() => WhatsAppContact.Create(WaId, "AR 1102953142229032", null, Now));
    }

    [Fact]
    public void Long_profile_names_are_truncated()
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, new string('a', 1000), Now);

        Assert.Equal(WhatsAppContact.MaxProfileNameLength, contact.ProfileName!.Length);
    }

    /// <summary>El nombre se corta sin partir un emoji, como el texto de un mensaje.</summary>
    [Fact]
    public void A_long_profile_name_is_cut_without_splitting_an_emoji()
    {
        var name = new string('a', WhatsAppContact.MaxProfileNameLength - 1) + "😀";

        var contact = WhatsAppContact.Create(WaId, Bsuid, name, Now);

        Assert.Equal(new string('a', WhatsAppContact.MaxProfileNameLength - 1), contact.ProfileName);
    }

    /// <summary>Postgres no guarda el carácter nulo: el nombre se limpia, y si no queda nada, se conserva el anterior.</summary>
    [Theory]
    [InlineData("\0Ana\0", "Ana")]
    [InlineData("\0 \0", null)]
    public void A_profile_name_without_null_characters(string profileName, string? expected)
    {
        var created = WhatsAppContact.Create(WaId, Bsuid, profileName, Now);
        var updated = WhatsAppContact.Create(WaId, Bsuid, "Ana María", Now);
        updated.RecordInbound(WaId, Bsuid, profileName, Now.AddMinutes(1));

        Assert.Equal(expected, created.ProfileName);
        Assert.Equal(expected ?? "Ana María", updated.ProfileName);
    }

    /// <summary>
    /// Media letra suelta no se puede mandar a Postgres: se cambia por el carácter de reemplazo (U+FFFD). Se arma en el
    /// código y no en un InlineData, porque los textos de un atributo se guardan en UTF-8 y una mitad suelta no
    /// sobrevive el viaje.
    /// </summary>
    [Fact]
    public void A_loose_half_of_an_emoji_in_the_profile_name_is_stored_as_the_replacement_character()
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, " Ana " + (char)0xD83D, Now);

        Assert.Equal("Ana " + (char)0xFFFD, contact.ProfileName);
    }

    [Fact]
    public void A_newer_message_updates_the_name_the_wa_id_and_the_last_inbound()
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, "Ana", Now);

        contact.RecordInbound("5493415550000", Bsuid, "Ana Pérez", Now.AddMinutes(1));

        Assert.Equal("5493415550000", contact.WaId);
        Assert.Equal("Ana Pérez", contact.ProfileName);
        Assert.Equal(Now.AddMinutes(1), contact.LastInboundAtUtc);
    }

    /// <summary>Meta reintenta durante días y manda los eventos desordenados: uno viejo no pisa lo que llegó después.</summary>
    [Fact]
    public void An_older_message_does_not_overwrite_what_a_newer_one_left()
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, "Ana Pérez", Now);

        contact.RecordInbound("5493415550000", Bsuid, "Ana", Now.AddMinutes(-5));

        Assert.Equal(WaId, contact.WaId);
        Assert.Equal("Ana Pérez", contact.ProfileName);
        Assert.Equal(Now, contact.LastInboundAtUtc);
    }

    [Fact]
    public void An_older_message_still_fills_what_was_missing()
    {
        var contact = WhatsAppContact.Create(waId: null, Bsuid, profileName: null, Now);

        contact.RecordInbound(WaId, Bsuid, "Ana", Now.AddMinutes(-5));

        Assert.Equal(WaId, contact.WaId);
        Assert.Equal("Ana", contact.ProfileName);
        Assert.Equal(Now, contact.LastInboundAtUtc);
    }

    [Fact]
    public void A_message_without_a_name_keeps_the_one_it_had()
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, "Ana", Now);

        contact.RecordInbound(WaId, userIdentifier: null, profileName: null, Now.AddMinutes(1));

        Assert.Equal("Ana", contact.ProfileName);
        Assert.Equal(Bsuid, contact.UserIdentifier);
    }

    /// <summary>Un contacto que hasta ahora llegaba solo con el número adopta el BSUID la primera vez que viene con él.</summary>
    [Fact]
    public void A_contact_found_by_its_number_adopts_the_bsuid()
    {
        var contact = WhatsAppContact.Create(WaId, userIdentifier: null, "Ana", Now);

        contact.RecordInbound(WaId, Bsuid, "Ana", Now.AddMinutes(-1));

        Assert.Equal(Bsuid, contact.UserIdentifier);
    }

    /// <summary>
    /// Otro BSUID es otra persona, o la misma con otro número (sección 6.5 del spec): aparece como un contacto nuevo y
    /// este no cambia de identidad.
    /// </summary>
    [Fact]
    public void A_contact_never_changes_its_bsuid()
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, "Ana", Now);

        Assert.Throws<InvalidOperationException>(() => contact.RecordInbound(WaId, "AR.999", "Ana", Now.AddMinutes(1)));
    }

    [Fact]
    public void The_bot_links_the_contact_to_its_account()
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, "Ana", Now);
        var userId = Guid.CreateVersion7();

        contact.LinkUser(userId);

        Assert.Equal(userId, contact.UserId);
    }

    [Fact]
    public void A_contact_is_linked_to_an_account_that_exists()
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, "Ana", Now);

        Assert.Throws<ArgumentException>(() => contact.LinkUser(Guid.Empty));
        Assert.Null(contact.UserId);
    }

    /// <summary>Una cuenta tiene un solo contacto: cuando otro contacto pasa a ser el de la cuenta, este la suelta.</summary>
    [Fact]
    public void Unlinking_leaves_the_contact_without_an_account()
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, "Ana", Now);
        contact.LinkUser(Guid.CreateVersion7());

        contact.UnlinkUser();

        Assert.Null(contact.UserId);
    }
}
