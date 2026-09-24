using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.WhatsApp;
using ArquitecturaBase.Application.Features.WhatsApp.HandleInboundMessage;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Features.WhatsApp;

/// <summary>
/// La tabla del bot, fila por fila (sección 8 del spec del ingreso con WhatsApp), con los textos del tablero
/// "WhatsApp · Conversaciones con el bot". Cada test deja pendientes los mensajes de un contacto, como los guarda el
/// webhook, y le pide al bot que los procese.
/// </summary>
public sealed class HandleInboundMessageTests
{
    private const string AppName = "Arquitectura Base";
    private const string WaId = "5493413654813";
    private const string Bsuid = "AR.1102953142229032";
    private const string FirstLinkUrl = "https://app.test/ingresar#t=token-1";
    private const string WebLoginUrl = "https://app.test/login";

    private const string SignInForAna =
        "Hola, Ana. Para entrar a Arquitectura Base, tocá Entrar. El enlace sirve una vez y vence en 10 minutos.";

    private const string Footer = "Por ahora este chat solo sirve para entrar.";
    private const string Disabled = "Tu cuenta está deshabilitada. Contactá a un administrador.";

    private static readonly PhoneNumber Phone = PhoneNumber.Create("+" + WaId).Value;
    private static readonly Uri Origin = new("https://app.test/");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly LockLog _locks = new();
    private readonly InMemoryWhatsAppContactRepository _contacts;
    private readonly InMemoryWhatsAppMessageRepository _messages;
    private readonly FakeIdentityService _identity = new();
    private readonly InMemoryLoginLinkRepository _loginLinks = new();
    private readonly FakeSecureTokenGenerator _tokens = new();
    private readonly FakeSystemSettingsReader _settings = new() { Mode = RegistrationMode.Open };
    private readonly FakeWhatsAppOutbox _outbox = new();
    private int _messageCount;

    public HandleInboundMessageTests()
    {
        _contacts = new InMemoryWhatsAppContactRepository(_locks);
        _messages = new InMemoryWhatsAppMessageRepository(_locks);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    // Fila 1: cuenta activa.
    [Fact]
    public async Task An_active_account_gets_a_new_link_its_contact_linked_and_its_number_verified()
    {
        var ana = await AccountWithPhoneAsync("Ana", confirmed: false);
        var contact = Contact("Ana Pérez");
        var hola = Text(contact, "Hola");

        var result = await HandleAsync(contact);

        Assert.True(result.IsSuccess);
        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(_outbox.Messages));
        Assert.Equal(Phone, reply.To);
        Assert.Equal(SignInForAna, reply.Body);
        Assert.Equal("Entrar", reply.ButtonText);
        Assert.Equal(FirstLinkUrl, reply.Url);
        Assert.Equal(Footer, reply.Footer);

        Assert.Equal(ana.Id, Assert.Single(_loginLinks.Links).UserId);
        Assert.Equal(ana.Id, contact.UserId);
        Assert.True(Account(ana.Id).PhoneNumberConfirmed);
        Assert.Equal(Now, hola.ProcessedAtUtc);

        // La regla de oro (sección 5 del spec): un mensaje de WhatsApp nunca abre una sesión.
        Assert.Empty(_identity.SignedInUsers);
    }

    /// <summary>
    /// El orden de la búsqueda (sección 8 del spec): primero la cuenta del contacto vinculado y después la del número.
    /// Acá las dos existen y son distintas, así que un bot que mirara primero el número le mandaría el enlace de Beto.
    /// </summary>
    [Fact]
    public async Task The_account_of_a_linked_contact_is_found_before_looking_at_the_number()
    {
        var ana = _identity.AddUser("ana@example.com");
        await _identity.SetDisplayNameAsync(ana.Id, "Ana", Ct);
        var beto = await AccountWithPhoneAsync("Beto", confirmed: false);
        var contact = Contact("Ana");
        contact.LinkUser(ana.Id);
        Text(contact, "Hola");

        await HandleAsync(contact);

        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(_outbox.Messages));
        Assert.Equal(SignInForAna, reply.Body);
        Assert.Equal(ana.Id, Assert.Single(_loginLinks.Links).UserId);
        Assert.Equal(ana.Id, contact.UserId);

        // Ningún número se toca: el de Ana (ninguno) no es el del chat, y la cuenta de Beto el bot ni la mira.
        Assert.Null(Account(ana.Id).PhoneNumber);
        Assert.False(Account(beto.Id).PhoneNumberConfirmed);
    }

    [Fact]
    public async Task Without_a_name_in_the_account_the_greeting_uses_the_whatsapp_profile_name()
    {
        await AccountWithPhoneAsync(name: null, confirmed: true);
        var contact = Contact("Ana");
        Text(contact, "Hola");

        await HandleAsync(contact);

        Assert.Equal(SignInForAna, Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(_outbox.Messages)).Body);
    }

    [Fact]
    public async Task Without_any_name_the_greeting_goes_without_one()
    {
        await AccountWithPhoneAsync(name: null, confirmed: true);
        var contact = Contact(profileName: null);
        Text(contact, "Hola");

        await HandleAsync(contact);

        Assert.Equal(
            "Hola. Para entrar a Arquitectura Base, tocá Entrar. El enlace sirve una vez y vence en 10 minutos.",
            Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(_outbox.Messages)).Body);
    }

    // Fila 2: deshabilitada, bloqueada o borrada.
    [Theory]
    [InlineData("disabled")]
    [InlineData("locked out")]
    [InlineData("deleted")]
    public async Task A_disabled_locked_out_or_deleted_account_is_told_to_contact_an_administrator(string state)
    {
        var ana = await AccountWithPhoneAsync("Ana", confirmed: false);
        await PutInStateAsync(ana.Id, state);
        var contact = Contact("Ana");
        var hola = Text(contact, "Hola");

        await HandleAsync(contact);

        var reply = Assert.IsType<WhatsAppTextMessage>(Assert.Single(_outbox.Messages));
        Assert.Equal(Phone, reply.To);
        Assert.Equal(Disabled, reply.Body);

        // Nada cambia.
        Assert.Empty(_loginLinks.Links);
        Assert.Null(contact.UserId);
        Assert.DoesNotContain(_identity.Users, user => user.PhoneNumberConfirmed);
        Assert.Equal(Now, hola.ProcessedAtUtc);
    }

    // Fila 3: sin cuenta, registro abierto.
    [Fact]
    public async Task Without_an_account_and_with_open_registration_it_asks_whether_to_create_one()
    {
        var contact = Contact("Ana");
        var hola = Text(contact, "Hola");

        await HandleAsync(contact);

        var reply = Assert.IsType<WhatsAppReplyButtonsMessage>(Assert.Single(_outbox.Messages));
        Assert.Equal(Phone, reply.To);
        Assert.Equal("Hola. No encontramos una cuenta con este número. ¿Querés crear una?", reply.Body);
        Assert.Equal(
            [(BotButtons.CreateAccount, "Crear cuenta"), (BotButtons.HaveAccount, "Ya tengo cuenta")],
            reply.Buttons.Select(button => (button.Id, button.Title)));
        Assert.Equal("CREATE_ACCOUNT", BotButtons.CreateAccount);
        Assert.Equal("HAVE_ACCOUNT", BotButtons.HaveAccount);

        Assert.Empty(_identity.Users);
        Assert.Empty(_loginLinks.Links);
        Assert.Null(contact.UserId);
        Assert.Equal(Now, hola.ProcessedAtUtc);
    }

    // Fila 4: «Crear cuenta».
    [Fact]
    public async Task Create_account_creates_it_with_the_verified_number_and_the_profile_name_and_sends_the_link()
    {
        var contact = Contact("Ana");
        var button = Button(contact, BotButtons.CreateAccount, "Crear cuenta");

        await HandleAsync(contact);

        var account = Assert.Single(_identity.Users);
        Assert.Null(account.Email);
        Assert.Equal(Phone.Value, account.PhoneNumber);
        Assert.True(account.PhoneNumberConfirmed);
        Assert.Equal("Ana", account.DisplayName);
        Assert.Equal("es", account.Culture);
        Assert.Equal(account.Id, contact.UserId);

        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(_outbox.Messages));
        Assert.Equal(
            "Listo, Ana: creamos tu cuenta con este número. Tocá Entrar para abrirla. El enlace sirve una vez y vence en 10 minutos.",
            reply.Body);
        Assert.Equal("Entrar", reply.ButtonText);
        Assert.Equal(FirstLinkUrl, reply.Url);
        Assert.Null(reply.Footer);

        Assert.Equal(account.Id, Assert.Single(_loginLinks.Links).UserId);
        Assert.Equal(Now, button.ProcessedAtUtc);
        Assert.Empty(_identity.SignedInUsers);
    }

    [Fact]
    public async Task Create_account_without_a_profile_name_creates_it_without_a_name()
    {
        var contact = Contact(profileName: null);
        Button(contact, BotButtons.CreateAccount, "Crear cuenta");

        await HandleAsync(contact);

        Assert.Null(Assert.Single(_identity.Users).DisplayName);
        Assert.Equal(
            "Listo: creamos tu cuenta con este número. Tocá Entrar para abrirla. El enlace sirve una vez y vence en 10 minutos.",
            Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(_outbox.Messages)).Body);
    }

    /// <summary>El botón tocado dos veces: la cuenta ya existe, así que se comporta como la primera fila.</summary>
    [Fact]
    public async Task Create_account_tapped_again_does_not_create_another_account_and_answers_like_an_active_one()
    {
        var contact = Contact("Ana");
        Button(contact, BotButtons.CreateAccount, "Crear cuenta");
        await HandleAsync(contact);
        _clock.Advance(TimeSpan.FromMinutes(2));

        Button(contact, BotButtons.CreateAccount, "Crear cuenta");
        await HandleAsync(contact);

        var account = Assert.Single(_identity.Users);
        Assert.Equal(2, _outbox.Messages.Count);
        var second = Assert.IsType<WhatsAppLinkButtonMessage>(_outbox.Messages[1]);
        Assert.Equal(SignInForAna, second.Body);
        Assert.Equal("https://app.test/ingresar#t=token-2", second.Url);
        Assert.All(_loginLinks.Links, link => Assert.Equal(account.Id, link.UserId));
    }

    [Fact]
    public async Task Create_account_after_registration_became_invite_only_answers_that_there_is_no_access()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        var contact = Contact("Ana");
        Button(contact, BotButtons.CreateAccount, "Crear cuenta");

        await HandleAsync(contact);

        Assert.Empty(_identity.Users);
        Assert.Empty(_loginLinks.Links);
        Assert.StartsWith(
            "Hola. Todavía no tenés acceso a Arquitectura Base",
            Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(_outbox.Messages)).Body,
            StringComparison.Ordinal);
    }

    // Fila 5: «Ya tengo cuenta».
    [Fact]
    public async Task Have_account_points_to_the_web_to_link_this_whatsapp_from_the_profile()
    {
        var contact = Contact("Ana");
        var button = Button(contact, BotButtons.HaveAccount, "Ya tengo cuenta");

        await HandleAsync(contact);

        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(_outbox.Messages));
        Assert.Equal(
            "Entrá a la web con tu correo y vinculá este WhatsApp desde Mi perfil. Después vas a poder entrar desde acá.",
            reply.Body);
        Assert.Equal("Ir a la web", reply.ButtonText);
        Assert.Equal(WebLoginUrl, reply.Url);
        Assert.Null(reply.Footer);

        Assert.Empty(_identity.Users);
        Assert.Empty(_loginLinks.Links);
        Assert.Equal(Now, button.ProcessedAtUtc);
    }

    // Fila 6: sin cuenta, solo por invitación.
    [Fact]
    public async Task Without_an_account_and_invite_only_registration_it_says_there_is_no_access_yet()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        var contact = Contact("Ana");
        var hola = Text(contact, "Hola, quiero entrar");

        await HandleAsync(contact);

        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(_outbox.Messages));
        Assert.Equal(
            "Hola. Todavía no tenés acceso a Arquitectura Base: pedile a un administrador que te dé de alta. Si ya tenés una cuenta con tu correo, podés vincular este WhatsApp desde Mi perfil.",
            reply.Body);
        Assert.Equal("Ir a la web", reply.ButtonText);
        Assert.Equal(WebLoginUrl, reply.Url);

        Assert.Empty(_identity.Users);
        Assert.Empty(_loginLinks.Links);
        Assert.Null(contact.UserId);
        Assert.Equal(Now, hola.ProcessedAtUtc);
    }

    // Fila 7: «Quiero entrar», el botón de la invitación.
    [Fact]
    public async Task Want_to_enter_from_the_invitation_sends_the_link_and_verifies_the_number_an_admin_loaded()
    {
        _settings.Mode = RegistrationMode.InviteOnly;
        var ana = await AccountWithPhoneAsync("Ana", confirmed: false);
        var contact = Contact("Ana");
        TemplateButton(contact, BotButtons.WantToEnter, "Quiero entrar");

        await HandleAsync(contact);

        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(_outbox.Messages));
        Assert.Equal(SignInForAna, reply.Body);
        Assert.Equal(FirstLinkUrl, reply.Url);
        Assert.Equal(Footer, reply.Footer);
        Assert.Equal(ana.Id, contact.UserId);
        Assert.True(Account(ana.Id).PhoneNumberConfirmed);
        Assert.Equal("WANT_TO_ENTER", BotButtons.WantToEnter);
    }

    // Fila 8: pidió un enlace hace menos de un minuto.
    [Fact]
    public async Task Asking_for_another_link_within_a_minute_is_told_to_wait()
    {
        await AccountWithPhoneAsync("Ana", confirmed: true);
        var contact = Contact("Ana");
        Text(contact, "Hola");
        await HandleAsync(contact);
        _clock.Advance(TimeSpan.FromSeconds(20));

        var again = Text(contact, "Mandame otro");
        await HandleAsync(contact);

        Assert.Equal(2, _outbox.Messages.Count);
        var reply = Assert.IsType<WhatsAppTextMessage>(_outbox.Messages[1]);
        Assert.Equal("Esperá un momento antes de pedir otro enlace. El que te mandamos sirve por 10 minutos.", reply.Body);

        // Nada cambia: el primero sigue sirviendo.
        Assert.True(Assert.Single(_loginLinks.Links).IsActive(Now));
        Assert.Equal(Now, again.ProcessedAtUtc);
    }

    // Fila 9: una foto, un audio, un sticker o lo que sea.
    [Theory]
    [InlineData(WhatsAppMessageKind.Media)]
    [InlineData(WhatsAppMessageKind.Other)]
    public async Task A_photo_an_audio_or_anything_else_is_answered_like_a_text(WhatsAppMessageKind kind)
    {
        var ana = await AccountWithPhoneAsync("Ana", confirmed: true);
        var contact = Contact("Ana");
        var photo = Inbound(contact, kind, body: null, replyId: null);

        await HandleAsync(contact);

        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(_outbox.Messages));
        Assert.Equal(SignInForAna, reply.Body);
        Assert.Equal(ana.Id, Assert.Single(_loginLinks.Links).UserId);
        Assert.Equal(Now, photo.ProcessedAtUtc);
    }

    [Fact]
    public async Task A_photo_without_an_account_gets_the_question_like_a_text()
    {
        var contact = Contact("Ana");
        Inbound(contact, WhatsAppMessageKind.Media, body: null, replyId: null);

        await HandleAsync(contact);

        Assert.IsType<WhatsAppReplyButtonsMessage>(Assert.Single(_outbox.Messages));
    }

    /// <summary>
    /// «No pedí un código», de la plantilla de autenticación: quien lo toca no pidió nada, así que no se le contesta.
    /// Queda procesado, y el código vence solo.
    /// </summary>
    [Fact]
    public async Task Did_not_request_a_code_is_marked_processed_without_a_reply()
    {
        await AccountWithPhoneAsync("Ana", confirmed: true);
        var contact = Contact("Ana");
        var button = TemplateButton(contact, BotButtons.DidNotRequestCode, "No pedí un código");

        var result = await HandleAsync(contact);

        Assert.True(result.IsSuccess);
        Assert.Empty(_outbox.Messages);
        Assert.Empty(_loginLinks.Links);
        Assert.Null(contact.UserId);
        Assert.Equal(Now, button.ProcessedAtUtc);
    }

    /// <summary>Un aviso de WhatsApp, como el cambio de número: se registra y no se contesta (queda para producción).</summary>
    [Fact]
    public async Task A_system_message_is_marked_processed_without_a_reply()
    {
        var contact = Contact("Ana");
        var notice = Inbound(contact, WhatsAppMessageKind.System, "User Ana changed from 5493413654813 to 5493415550000", replyId: null);

        await HandleAsync(contact);

        Assert.Empty(_outbox.Messages);
        Assert.Equal(Now, notice.ProcessedAtUtc);
    }

    [Fact]
    public async Task An_account_in_english_gets_the_texts_in_english()
    {
        await AccountWithPhoneAsync("Ana", confirmed: true, culture: "en");
        var contact = Contact("Ana");
        Text(contact, "Hi");

        await HandleAsync(contact);

        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(_outbox.Messages));
        Assert.Equal("Hi, Ana. To sign in to Arquitectura Base, tap Sign in. The link works once and expires in 10 minutes.", reply.Body);
        Assert.Equal("Sign in", reply.ButtonText);
        Assert.Equal("For now, this chat is only for signing in.", reply.Footer);
    }

    [Fact]
    public async Task A_disabled_account_in_english_is_told_in_english()
    {
        var ana = await AccountWithPhoneAsync("Ana", confirmed: true, culture: "en");
        await _identity.SetActiveAsync(ana.Id, isActive: false, Ct);
        var contact = Contact("Ana");
        Text(contact, "Hi");

        await HandleAsync(contact);

        Assert.Equal(
            "Your account is disabled. Contact an administrator.",
            Assert.IsType<WhatsAppTextMessage>(Assert.Single(_outbox.Messages)).Body);
    }

    /// <summary>
    /// Una cuenta borrada conserva su número y su idioma, y el bot la trata como deshabilitada (sección 6.1 del spec):
    /// también en el idioma. Ninguna búsqueda común la encuentra, pero no es "sin cuenta", que va en español.
    /// </summary>
    [Fact]
    public async Task A_deleted_account_in_english_is_told_in_english()
    {
        var ana = await AccountWithPhoneAsync("Ana", confirmed: true, culture: "en");
        await _identity.DeleteAsync(ana.Id, Ct);
        var contact = Contact("Ana");
        Text(contact, "Hi");

        await HandleAsync(contact);

        Assert.Equal(
            "Your account is disabled. Contact an administrator.",
            Assert.IsType<WhatsAppTextMessage>(Assert.Single(_outbox.Messages)).Body);
    }

    /// <summary>
    /// Meta no deja mandarle a la misma persona más de un mensaje cada 6 segundos: una ráfaga recibe una sola respuesta,
    /// decidida por el más nuevo que tenga algo que decidir. Un botón gana sobre un texto.
    /// </summary>
    [Fact]
    public async Task A_burst_gets_a_single_reply_decided_by_the_newest_button()
    {
        var contact = Contact("Ana");
        var hola = Text(contact, "Hola", Now.AddSeconds(-3));
        var button = Button(contact, BotButtons.CreateAccount, "Crear cuenta", Now.AddSeconds(-2));
        var question = Text(contact, "¿Ya está?", Now.AddSeconds(-1));

        await HandleAsync(contact);

        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(_outbox.Messages));
        Assert.StartsWith("Listo, Ana: creamos tu cuenta", reply.Body, StringComparison.Ordinal);
        Assert.Single(_identity.Users);
        Assert.All([hola, button, question], message => Assert.Equal(Now, message.ProcessedAtUtc));
    }

    [Fact]
    public async Task Among_several_buttons_the_newest_one_decides()
    {
        var contact = Contact("Ana");
        Button(contact, BotButtons.CreateAccount, "Crear cuenta", Now.AddSeconds(-5));
        Button(contact, BotButtons.HaveAccount, "Ya tengo cuenta", Now.AddSeconds(-1));

        await HandleAsync(contact);

        Assert.Empty(_identity.Users);
        Assert.Equal("Ir a la web", Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(_outbox.Messages)).ButtonText);
    }

    /// <summary>
    /// Fuera de las 24 horas, Meta rechaza todo lo que no sea una plantilla: un mensaje viejo (un reintento de Meta
    /// después de una caída) queda procesado sin respuesta.
    /// </summary>
    [Fact]
    public async Task A_message_older_than_24_hours_is_marked_processed_without_a_reply()
    {
        await AccountWithPhoneAsync("Ana", confirmed: true);
        var contact = Contact("Ana");
        var old = Text(contact, "Hola", Now.AddHours(-24).AddSeconds(-1));

        await HandleAsync(contact);

        Assert.Empty(_outbox.Messages);
        Assert.Empty(_loginLinks.Links);
        Assert.Equal(Now, old.ProcessedAtUtc);
    }

    [Fact]
    public async Task An_old_button_outside_the_24_hours_does_not_decide_the_reply()
    {
        var contact = Contact("Ana");
        var old = Button(contact, BotButtons.CreateAccount, "Crear cuenta", Now.AddDays(-2));
        var hola = Text(contact, "Hola", Now.AddSeconds(-1));

        await HandleAsync(contact);

        Assert.Empty(_identity.Users);
        Assert.IsType<WhatsAppReplyButtonsMessage>(Assert.Single(_outbox.Messages));
        Assert.Equal(Now, old.ProcessedAtUtc);
        Assert.Equal(Now, hola.ProcessedAtUtc);
    }

    /// <summary>Otra instancia tiene el contacto: sus mensajes siguen pendientes para la próxima vuelta.</summary>
    [Fact]
    public async Task A_contact_that_another_instance_is_processing_is_left_pending()
    {
        await AccountWithPhoneAsync("Ana", confirmed: true);
        var contact = Contact("Ana");
        var hola = Text(contact, "Hola");
        _contacts.LockedElsewhere.Add(contact.Id);

        var result = await HandleAsync(contact);

        Assert.True(result.IsSuccess);
        Assert.Empty(_outbox.Messages);
        Assert.Null(hola.ProcessedAtUtc);
    }

    /// <summary>El lock del contacto se toma antes de leer sus mensajes: si no, otra instancia podría leer los mismos.</summary>
    [Fact]
    public async Task The_contact_is_locked_before_reading_its_messages()
    {
        var contact = Contact("Ana");
        Text(contact, "Hola");

        await HandleAsync(contact);

        Assert.Equal(["processing:" + contact.Id, "read:ListPendingInboundAsync"], _locks.Events.Take(2));
    }

    [Fact]
    public async Task A_contact_without_pending_messages_gets_no_reply()
    {
        var contact = Contact("Ana");

        var result = await HandleAsync(contact);

        Assert.True(result.IsSuccess);
        Assert.Empty(_outbox.Messages);
    }

    /// <summary>Cuando WhatsApp oculte los números, un contacto puede llegar solo con el BSUID: sin número no hay a quién responder.</summary>
    [Fact]
    public async Task A_contact_without_a_number_is_marked_processed_without_a_reply()
    {
        var contact = WhatsAppContact.Create(waId: null, Bsuid, "Ana", Now);
        _contacts.Contacts.Add(contact);
        var hola = Text(contact, "Hola");

        await HandleAsync(contact);

        Assert.Empty(_outbox.Messages);
        Assert.Equal(Now, hola.ProcessedAtUtc);
    }

    /// <summary>
    /// Una cuenta tiene un solo contacto. Si el número de la cuenta le escribe con otro BSUID (WhatsApp le dio uno
    /// nuevo), el contacto nuevo pasa a ser el de la cuenta y el viejo la suelta.
    /// </summary>
    [Fact]
    public async Task A_new_contact_with_the_number_of_an_account_takes_over_its_link()
    {
        var ana = await AccountWithPhoneAsync("Ana", confirmed: true);
        var previous = WhatsAppContact.Create(WaId, "AR.1000000000000001", "Ana", Now.AddDays(-30));
        previous.LinkUser(ana.Id);
        _contacts.Contacts.Add(previous);
        var contact = Contact("Ana");
        Text(contact, "Hola");

        await HandleAsync(contact);

        Assert.Null(previous.UserId);
        Assert.Equal(ana.Id, contact.UserId);
    }

    /// <summary>
    /// El bot encuentra la cuenta por el número antes de tener su lock. Si mientras lo espera la cuenta suelta el número
    /// (la persona lo desvinculó o lo cambió por otro desde el perfil), el chat ya no es de esa cuenta: ni recibe un
    /// enlace de ella ni queda vinculado a ella, y se le contesta como a un número sin cuenta.
    /// </summary>
    [Theory]
    [InlineData("removed")]
    [InlineData("replaced")]
    public async Task An_account_that_lets_go_of_the_number_while_the_bot_waits_for_it_gets_neither_a_link_nor_the_chat(string change)
    {
        var ana = await AccountWithPhoneAsync("Ana", confirmed: true);
        var contact = Contact("Ana");
        var hola = Text(contact, "Hola");
        _loginLinks.WhileWaitingForTheLock = userId => change == "removed"
            ? _identity.RemovePhoneAsync(userId, Ct)
            : _identity.SetPhoneAsync(userId, PhoneNumber.Create("+5493410000000").Value, confirmed: true, Ct);

        await HandleAsync(contact);

        var reply = Assert.IsType<WhatsAppReplyButtonsMessage>(Assert.Single(_outbox.Messages));
        Assert.Equal("Hola. No encontramos una cuenta con este número. ¿Querés crear una?", reply.Body);
        Assert.Equal([ana.Id], _loginLinks.LockedAccounts.Distinct());
        Assert.Empty(_loginLinks.Links);
        Assert.Null(contact.UserId);
        Assert.Equal(Now, hola.ProcessedAtUtc);
    }

    /// <summary>
    /// Si la cola no toma la respuesta, falla: la unidad de trabajo no guarda nada, los mensajes siguen pendientes y el
    /// procesador los vuelve a intentar. Marcarlos procesados sería dejar a la persona sin respuesta.
    /// </summary>
    [Fact]
    public async Task When_the_queue_does_not_take_the_reply_it_fails_so_nothing_is_saved()
    {
        await AccountWithPhoneAsync("Ana", confirmed: true);
        var contact = Contact("Ana");
        Text(contact, "Hola");
        _outbox.Accepts = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => HandleAsync(contact));
    }

    private HandleInboundMessageCommandHandler Handler() =>
        new(
            _contacts,
            new WhatsAppContactLinker(_contacts),
            _messages,
            _identity,
            new FakePhoneNumberParser(),
            _loginLinks,
            new LoginLinkIssuer(
                _loginLinks,
                _tokens,
                new FakePublicOrigin(Origin),
                Options.Create(new LoginLinkOptions()),
                _clock),
            new AccountCreationPolicy(_settings, new FakeInitialAdmin()),
            new FakePublicOrigin(Origin),
            new FakeAppName(AppName),
            _outbox,
            _clock,
            NullLogger<HandleInboundMessageCommandHandler>.Instance);

    private Task<Result> HandleAsync(WhatsAppContact contact) =>
        Handler().Handle(new HandleInboundMessageCommand(contact.Id), Ct);

    /// <summary>La persona que escribe desde <see cref="Phone"/>, ya guardada por el webhook.</summary>
    private WhatsAppContact Contact(string? profileName)
    {
        var contact = WhatsAppContact.Create(WaId, Bsuid, profileName, Now.AddMinutes(-1));
        _contacts.Contacts.Add(contact);

        return contact;
    }

    private WhatsAppMessage Text(WhatsAppContact contact, string body, DateTime? atUtc = null) =>
        Inbound(contact, WhatsAppMessageKind.Text, body, replyId: null, atUtc);

    private WhatsAppMessage Button(WhatsAppContact contact, string id, string title, DateTime? atUtc = null) =>
        Inbound(contact, WhatsAppMessageKind.ButtonReply, title, id, atUtc);

    // Un botón de plantilla llega igual que uno de respuesta: el payload queda en ReplyId (Tarea 8).
    private WhatsAppMessage TemplateButton(WhatsAppContact contact, string payload, string text) =>
        Inbound(contact, WhatsAppMessageKind.ButtonReply, text, payload);

    private WhatsAppMessage Inbound(WhatsAppContact contact, WhatsAppMessageKind kind, string? body, string? replyId, DateTime? atUtc = null)
    {
        var message = WhatsAppMessage.Inbound(contact.Id, "wamid.test-" + ++_messageCount, kind, body, replyId, atUtc ?? Now);
        _messages.Messages.Add(message);

        return message;
    }

    /// <summary>Una cuenta con el número del chat, como la deja el ingreso por código o el alta de un administrador.</summary>
    private async Task<UserAccount> AccountWithPhoneAsync(string? name, bool confirmed, string culture = "es")
    {
        var account = _identity.AddUser(email: null, culture: culture, phoneNumber: Phone.Value);
        await _identity.SetPhoneAsync(account.Id, Phone, confirmed, Ct);

        if (name is not null)
        {
            await _identity.SetDisplayNameAsync(account.Id, name, Ct);
        }

        return Account(account.Id);
    }

    private async Task PutInStateAsync(Guid userId, string state)
    {
        switch (state)
        {
            case "disabled":
                await _identity.SetActiveAsync(userId, isActive: false, Ct);
                break;
            case "locked out":
                _identity.LockedOutUsers.Add(userId);
                break;
            case "deleted":
                await _identity.DeleteAsync(userId, Ct);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown account state.");
        }
    }

    private UserAccount Account(Guid userId) => _identity.Users.Single(user => user.Id == userId);
}
