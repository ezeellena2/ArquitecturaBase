using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Application.Features.WhatsApp.HandleInboundMessage;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;
using ArquitecturaBase.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// El bot de punta a punta (secciones 7 y 8 del spec del ingreso con WhatsApp): un webhook firmado como los de Meta,
/// después una vuelta del procesador (<see cref="WhatsAppInboundProcessor.ProcessPendingAsync"/>) y después lo que se
/// encoló para mandar (<c>factory.WhatsApp</c>). En el arnés el procesador no corre solo: cada test lo llama cuando
/// quiere. Todos los tests comparten la base, así que cada uno escribe desde un número propio y mira solo lo suyo.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppBotTests(ApiFactory factory)
{
    private const string Route = "/webhooks/whatsapp";
    private const string LinkPrefix = "https://localhost/ingresar#t=";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime Now => MetaWebhook.TruncatedToSeconds(factory.Clock.GetUtcNow().UtcDateTime);

    // Fila 1: un número con cuenta.
    [Fact]
    public async Task A_number_with_an_account_gets_the_sign_in_button_its_contact_linked_and_its_number_verified()
    {
        var person = Person.Unique();
        var account = await CreateAccountAsync(person.Phone, "Ana Pérez", phoneConfirmed: false);
        using var client = factory.CreateClient();

        using var webhook = await PostSignedAsync(client, person.Says("Hola", Now));
        await ProcessPendingAsync();

        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(factory.WhatsApp.SentTo(person.Phone)));
        Assert.Equal(
            "Hola, Ana Pérez. Para entrar a Arquitectura Base, tocá Entrar. El enlace sirve una vez y vence en 10 minutos.",
            reply.Body);
        Assert.Equal("Entrar", reply.ButtonText);
        Assert.StartsWith(LinkPrefix, reply.Url, StringComparison.Ordinal);
        Assert.Equal("Por ahora este chat solo sirve para entrar.", reply.Footer);

        // El enlace es de la cuenta: /ingresar muestra a quién lleva.
        using var preview = await client.PostJsonAsync("/account/login-link/preview", new { token = TokenOf(reply.Url) }, language: "es");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("Ana Pérez", (await preview.ReadJsonAsync()).GetProperty("displayName").GetString());

        var contact = await FindContactAsync(person);
        Assert.Equal(account.Id, contact.UserId);
        Assert.True((await FindAccountAsync(account.Id)).PhoneNumberConfirmed);
        Assert.All(await InboundOfAsync(contact), message => Assert.NotNull(message.ProcessedAtUtc));

        // La regla de oro (sección 5 del spec): el pedido de Meta nunca se lleva una sesión.
        Assert.False(webhook.Headers.Contains("Set-Cookie"));
    }

    // Fila 3: sin cuenta, con el registro abierto (el arnés arranca en Open).
    [Fact]
    public async Task A_number_without_an_account_and_open_registration_gets_the_question_with_two_buttons()
    {
        var person = Person.Unique();
        using var client = factory.CreateClient();

        using var response = await PostSignedAsync(client, person.Says("Hola", Now));
        await ProcessPendingAsync();

        var reply = Assert.IsType<WhatsAppReplyButtonsMessage>(Assert.Single(factory.WhatsApp.SentTo(person.Phone)));
        Assert.Equal("Hola. No encontramos una cuenta con este número. ¿Querés crear una?", reply.Body);
        Assert.Equal(
            [("CREATE_ACCOUNT", "Crear cuenta"), ("HAVE_ACCOUNT", "Ya tengo cuenta")],
            reply.Buttons.Select(button => (button.Id, button.Title)));
        Assert.Null(await FindAccountByPhoneAsync(person.Phone));
        Assert.Null((await FindContactAsync(person)).UserId);
    }

    // Fila 4: «Crear cuenta».
    [Fact]
    public async Task Create_account_creates_it_with_the_verified_number_and_the_profile_name_and_its_link_signs_in()
    {
        var person = Person.Unique();
        using var client = factory.CreateClient();

        using var response = await PostSignedAsync(client, person.Taps(BotButtons.CreateAccount, "Crear cuenta", Now));
        await ProcessPendingAsync();

        var account = Assert.IsType<UserAccount>(await FindAccountByPhoneAsync(person.Phone));
        Assert.Null(account.Email);
        Assert.True(account.PhoneNumberConfirmed);
        Assert.Equal("Ana Pérez", account.DisplayName);
        Assert.Equal("es", account.Culture);
        Assert.Equal(account.Id, (await FindContactAsync(person)).UserId);

        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(factory.WhatsApp.SentTo(person.Phone)));
        Assert.Equal(
            "Listo, Ana Pérez: creamos tu cuenta con este número. Tocá Entrar para abrirla. El enlace sirve una vez y vence en 10 minutos.",
            reply.Body);
        Assert.StartsWith(LinkPrefix, reply.Url, StringComparison.Ordinal);

        // La sesión la abre el navegador de la persona al canjear el enlace, nunca el webhook.
        using var redeem = await client.PostJsonAsync("/account/login-link/redeem", new { token = TokenOf(reply.Url) }, language: "es");
        Assert.Equal(HttpStatusCode.NoContent, redeem.StatusCode);
    }

    [Fact]
    public async Task Create_account_tapped_twice_creates_a_single_account_and_then_answers_like_an_active_one()
    {
        var person = Person.Unique();
        using var client = factory.CreateClient();

        using var first = await PostSignedAsync(client, person.Taps(BotButtons.CreateAccount, "Crear cuenta", Now));
        await ProcessPendingAsync();
        using var second = await PostSignedAsync(client, person.Taps(BotButtons.CreateAccount, "Crear cuenta", Now.AddSeconds(5)));
        await ProcessPendingAsync();

        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Users.CountAsync(user => user.PhoneNumber == person.Phone.Value, Ct)));

        var replies = factory.WhatsApp.SentTo(person.Phone);
        Assert.Equal(2, replies.Count);
        Assert.StartsWith("Listo, Ana Pérez", Assert.IsType<WhatsAppLinkButtonMessage>(replies[0]).Body, StringComparison.Ordinal);
        Assert.StartsWith("Hola, Ana Pérez. Para entrar", Assert.IsType<WhatsAppLinkButtonMessage>(replies[1]).Body, StringComparison.Ordinal);
    }

    // Fila 6: sin cuenta, con el registro solo por invitación.
    [Fact]
    public async Task A_number_without_an_account_and_invite_only_registration_is_told_it_has_no_access_yet()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        var person = Person.Unique();
        using var client = factory.CreateClient();

        using var response = await PostSignedAsync(client, person.Says("Hola, quiero entrar", Now));
        await ProcessPendingAsync();

        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(factory.WhatsApp.SentTo(person.Phone)));
        Assert.Equal(
            "Hola. Todavía no tenés acceso a Arquitectura Base: pedile a un administrador que te dé de alta. Si ya tenés una cuenta con tu correo, podés vincular este WhatsApp desde Mi perfil.",
            reply.Body);
        Assert.Equal("Ir a la web", reply.ButtonText);
        Assert.Equal("https://localhost/login", reply.Url);
        Assert.Null(await FindAccountByPhoneAsync(person.Phone));
    }

    // Fila 8: otro enlace antes del minuto. El arnés no tiene espera entre enlaces: esta Api usa el valor real.
    [Fact]
    public async Task Asking_for_another_link_within_a_minute_is_told_to_wait()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("Authentication:LoginLink:ResendCooldownSeconds", "60"));
        using var client = api.CreateClient();
        var processor = api.Services.GetRequiredService<WhatsAppInboundProcessor>();
        var person = Person.Unique();
        await CreateAccountAsync(person.Phone, "Ana Pérez", phoneConfirmed: true);

        using var first = await PostSignedAsync(client, person.Says("Hola", Now));
        await processor.ProcessPendingAsync(Ct);
        factory.Clock.Advance(TimeSpan.FromSeconds(20));
        using var second = await PostSignedAsync(client, person.Says("Mandame otro", Now));
        await processor.ProcessPendingAsync(Ct);

        var replies = factory.WhatsApp.SentTo(person.Phone);
        Assert.Equal(2, replies.Count);
        Assert.IsType<WhatsAppLinkButtonMessage>(replies[0]);
        Assert.Equal(
            "Esperá un momento antes de pedir otro enlace. El que te mandamos sirve por 10 minutos.",
            Assert.IsType<WhatsAppTextMessage>(replies[1]).Body);
    }

    /// <summary>Meta reintenta el webhook que no recibió su 200: el mensaje repetido no recibe otra respuesta.</summary>
    [Fact]
    public async Task A_repeated_webhook_gets_a_single_reply()
    {
        var person = Person.Unique();
        await CreateAccountAsync(person.Phone, "Ana Pérez", phoneConfirmed: true);
        var body = person.Says("Hola", Now);
        using var client = factory.CreateClient();

        using var first = await PostSignedAsync(client, body);
        using var repeated = await PostSignedAsync(client, body);
        await ProcessPendingAsync();
        using var late = await PostSignedAsync(client, body);
        await ProcessPendingAsync();

        Assert.Single(factory.WhatsApp.SentTo(person.Phone));
    }

    /// <summary>
    /// Meta no deja mandarle a la misma persona más de un mensaje cada 6 segundos: una ráfaga recibe una sola respuesta,
    /// y quedan procesados los tres.
    /// </summary>
    [Fact]
    public async Task A_burst_of_three_messages_from_the_same_contact_gets_a_single_reply()
    {
        var person = Person.Unique();
        await CreateAccountAsync(person.Phone, "Ana Pérez", phoneConfirmed: true);
        using var client = factory.CreateClient();

        using var first = await PostSignedAsync(client, person.Says("Hola", Now.AddSeconds(-2)));
        using var second = await PostSignedAsync(client, person.Says("¿Hay alguien?", Now.AddSeconds(-1)));
        using var third = await PostSignedAsync(client, person.Says("Quiero entrar", Now));
        await ProcessPendingAsync();

        Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(factory.WhatsApp.SentTo(person.Phone)));

        var inbound = await InboundOfAsync(await FindContactAsync(person));
        Assert.Equal(3, inbound.Count);
        Assert.All(inbound, message => Assert.NotNull(message.ProcessedAtUtc));
    }

    /// <summary>
    /// Un reintento de Meta después de una caída puede traer mensajes de hace más de 24 horas: fuera de la ventana, Meta
    /// rechaza todo lo que no sea una plantilla. Quedan procesados sin respuesta y sin enlace.
    /// </summary>
    [Fact]
    public async Task A_message_older_than_24_hours_gets_no_reply_and_is_marked_processed()
    {
        var person = Person.Unique();
        var account = await CreateAccountAsync(person.Phone, "Ana Pérez", phoneConfirmed: true);
        using var client = factory.CreateClient();

        using var response = await PostSignedAsync(client, person.Says("Hola", Now.AddHours(-25)));
        await ProcessPendingAsync();

        Assert.Empty(factory.WhatsApp.SentTo(person.Phone));
        Assert.NotNull(Assert.Single(await InboundOfAsync(await FindContactAsync(person))).ProcessedAtUtc);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.LoginLinks.AnyAsync(link => link.UserId == account.Id, Ct)));
    }

    /// <summary>
    /// Una cuenta tiene un solo contacto. Si su número escribe con otro BSUID (WhatsApp le dio uno nuevo), el contacto
    /// nuevo pasa a ser el de la cuenta y el viejo la suelta, en la misma transacción: el índice único de la cuenta no
    /// deja que las dos filas la tengan a la vez.
    /// </summary>
    [Fact]
    public async Task A_new_contact_with_the_number_of_an_account_takes_over_the_link_of_the_old_one()
    {
        var before = Person.Unique();
        var now = before with { Bsuid = MetaWebhook.UniqueBsuid() };
        var account = await CreateAccountAsync(before.Phone, "Ana Pérez", phoneConfirmed: true);
        using var client = factory.CreateClient();

        using var first = await PostSignedAsync(client, before.Says("Hola", Now.AddSeconds(-10)));
        await ProcessPendingAsync();
        using var second = await PostSignedAsync(client, now.Says("Hola de nuevo", Now));
        await ProcessPendingAsync();

        Assert.Null((await FindContactAsync(before)).UserId);
        Assert.Equal(account.Id, (await FindContactAsync(now)).UserId);
        Assert.Equal(2, factory.WhatsApp.SentTo(before.Phone).Count);
        Assert.Equal(2, await factory.ExecuteDbContextAsync(db => db.LoginLinks.CountAsync(link => link.UserId == account.Id, Ct)));
    }

    /// <summary>
    /// La cola de salida guarda cada mensaje que Meta aceptó, con el id que devolvió y su resumen seguro (nunca la URL
    /// del enlace), así el estado que avisa Meta después lo encuentra (sección 9 del spec). En el arnés la cola no sale
    /// a ningún lado: esta Api usa la Graph API de mentira, y el mensaje se encola en la cola de verdad.
    /// </summary>
    [Fact]
    public async Task The_sender_saves_each_outbound_with_its_meta_id_and_safe_summary_and_a_status_updates_it()
    {
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var meta = new FakeMetaHandler(_ => FakeMetaHandler.Success(waMessageId));
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(WhatsAppRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => meta)));
        using var client = api.CreateClient();
        var person = Person.Unique();
        using var hello = await PostSignedAsync(client, person.Says("Hola", Now.AddSeconds(-5)));
        var contact = await FindContactAsync(person);
        var message = new WhatsAppLinkButtonMessage(
            person.Phone,
            "Hola, Ana Pérez. Para entrar a Arquitectura Base, tocá Entrar.",
            "Entrar",
            "https://localhost/ingresar#t=secret-token",
            "Por ahora este chat solo sirve para entrar.");

        Assert.True(api.Services.GetRequiredService<WhatsAppOutbox>().TryEnqueue(message));
        var saved = await WaitForMessageAsync(waMessageId);

        Assert.Equal(1, meta.Calls);
        Assert.Equal(WhatsAppMessageDirection.Outbound, saved.Direction);
        Assert.Equal(contact.Id, saved.ContactId);
        Assert.Equal(WhatsAppMessageKind.Interactive, saved.Kind);
        Assert.Equal(message.SafeSummary, saved.Body);
        Assert.DoesNotContain("secret-token", saved.Body, StringComparison.Ordinal);
        Assert.Null(saved.Status);

        using var delivered = await PostSignedAsync(
            client,
            MetaWebhook.Build(statuses: [MetaWebhook.Status(waMessageId, "delivered", Now, person.WaId)]));

        var updated = await WaitForMessageAsync(waMessageId);
        Assert.Equal(WhatsAppMessageStatus.Delivered, updated.Status);
        Assert.Equal(Now, updated.StatusAtUtc);
    }

    /// <summary>
    /// Un error con un contacto (acá, la cola que no toma su respuesta) no frena a los demás ni tumba nada: sus mensajes
    /// siguen pendientes, sin nada a medio guardar, y la próxima vuelta le contesta.
    /// </summary>
    [Fact]
    public async Task A_failure_with_one_contact_does_not_stop_the_others_and_its_messages_stay_pending()
    {
        var failing = Person.Unique();
        var other = Person.Unique();
        var outbox = new RejectingOutbox(factory.WhatsApp, failing.Phone);
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWhatsAppOutbox>();
            services.AddSingleton<IWhatsAppOutbox>(outbox);
        }));
        using var client = api.CreateClient();
        var processor = api.Services.GetRequiredService<WhatsAppInboundProcessor>();

        // El que falla escribió primero: el procesador lo intenta antes que al otro.
        using var first = await PostSignedAsync(client, failing.Taps(BotButtons.CreateAccount, "Crear cuenta", Now.AddSeconds(-1)));
        using var second = await PostSignedAsync(client, other.Says("Hola", Now));
        await processor.ProcessPendingAsync(Ct);

        Assert.Empty(factory.WhatsApp.SentTo(failing.Phone));
        Assert.Single(factory.WhatsApp.SentTo(other.Phone));
        Assert.Null(Assert.Single(await InboundOfAsync(await FindContactAsync(failing))).ProcessedAtUtc);
        Assert.Null(await FindAccountByPhoneAsync(failing.Phone));

        outbox.Rejecting = false;
        await processor.ProcessPendingAsync(Ct);

        Assert.StartsWith(
            "Listo, Ana Pérez",
            Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(factory.WhatsApp.SentTo(failing.Phone))).Body,
            StringComparison.Ordinal);
        Assert.NotNull(await FindAccountByPhoneAsync(failing.Phone));
    }

    /// <summary>
    /// Varias instancias (sección 7 del spec), con el lock de verdad de Postgres: el contacto que una tiene tomado no lo
    /// lee ninguna otra, que lo saltea sin esperar y les contesta a los demás. Mientras tanto el webhook le sigue
    /// guardando mensajes, porque el lock del bot no traba la clave foránea de un mensaje nuevo. Cuando la primera lo
    /// suelta (acá, sin confirmar nada), la próxima vuelta le contesta una sola vez a todo lo que quedó.
    /// </summary>
    [Fact]
    public async Task A_contact_another_instance_has_taken_is_skipped_without_waiting_while_its_messages_keep_arriving()
    {
        var taken = Person.Unique();
        var other = Person.Unique();
        using var client = factory.CreateClient();
        using var hello = await PostSignedAsync(client, taken.Says("Hola", Now));
        var contact = await FindContactAsync(taken);

        await using (var first = factory.Services.CreateAsyncScope())
        {
            // La primera instancia toma el contacto y deja la transacción abierta, como mientras decide la respuesta.
            Assert.NotNull(await first.ServiceProvider.GetRequiredService<IWhatsAppContactRepository>()
                .GetForProcessingAsync(contact.Id, Ct));

            // Lo que sigue no espera al lock: si esperara, lo cortaría este plazo, porque el lock no se suelta hasta el
            // final del bloque.
            using var noWait = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            noWait.CancelAfter(TimeSpan.FromSeconds(10));

            await using (var second = factory.Services.CreateAsyncScope())
            {
                Assert.Null(await second.ServiceProvider.GetRequiredService<IWhatsAppContactRepository>()
                    .GetForProcessingAsync(contact.Id, noWait.Token));
            }

            // Uno que llega desordenado, más viejo que el último, no cambia el contacto: solo se inserta. El que sí lo
            // cambia (su último mensaje, su nombre) espera a que el bot lo suelte, como dice el repositorio.
            using var late = await PostSignedWithinAsync(client, taken.Says("¿Hay alguien?", Now.AddSeconds(-10)), noWait.Token);
            using var fromOther = await PostSignedWithinAsync(client, other.Says("Hola", Now), noWait.Token);

            await using var otherInstance = factory.WithWebHostBuilder(_ => { });
            await otherInstance.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);

            Assert.Empty(factory.WhatsApp.SentTo(taken.Phone));
            Assert.Single(factory.WhatsApp.SentTo(other.Phone));

            var pending = await InboundOfAsync(contact);
            Assert.Equal(2, pending.Count);
            Assert.All(pending, message => Assert.Null(message.ProcessedAtUtc));
        }

        await ProcessPendingAsync();

        Assert.Single(factory.WhatsApp.SentTo(taken.Phone));
        Assert.All(await InboundOfAsync(contact), message => Assert.NotNull(message.ProcessedAtUtc));
    }

    /// <summary>
    /// Fuera de los tests, el procesador corre solo: el webhook lo despierta después de guardar, sin esperar a la
    /// revisión periódica (que con el reloj de los tests no llega nunca).
    /// </summary>
    [Fact]
    public async Task A_webhook_wakes_the_processor_running_in_the_background()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:ProcessInboundInBackground", "true"));
        using var client = api.CreateClient();
        var person = Person.Unique();
        await CreateAccountAsync(person.Phone, "Ana Pérez", phoneConfirmed: true);

        using var response = await PostSignedAsync(client, person.Says("Hola", Now));

        await WaitUntilAsync(() => factory.WhatsApp.CountFor(person.Phone) == 1);
        Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(factory.WhatsApp.SentTo(person.Phone)));
    }

    private Task ProcessPendingAsync() =>
        factory.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);

    private static string TokenOf(string url) => url[LinkPrefix.Length..];

    private static Task<HttpResponseMessage> PostSignedAsync(HttpClient client, byte[] body) =>
        PostSignedWithinAsync(client, body, Ct);

    /// <summary>El mismo webhook firmado, con un plazo propio: para lo que no tiene que esperar a ningún lock.</summary>
    private static async Task<HttpResponseMessage> PostSignedWithinAsync(HttpClient client, byte[] body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Route, UriKind.Relative))
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));

        var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return response;
    }

    private Task<UserAccount> CreateAccountAsync(PhoneNumber phone, string displayName, bool phoneConfirmed) =>
        factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>()
            .CreateAsync(email: null, phone, phoneConfirmed, displayName, "es", Ct));

    private Task<UserAccount> FindAccountAsync(Guid userId) =>
        factory.ExecuteScopeAsync(async services =>
            Assert.IsType<UserAccount>(await services.GetRequiredService<IIdentityService>().FindByIdAsync(userId, Ct)));

    private Task<UserAccount?> FindAccountByPhoneAsync(PhoneNumber phone) =>
        factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>().FindByPhoneAsync(phone, Ct));

    private Task<WhatsAppContact> FindContactAsync(Person person) =>
        factory.ExecuteDbContextAsync(db => db.WhatsAppContacts.AsNoTracking().SingleAsync(contact => contact.UserIdentifier == person.Bsuid, Ct));

    private Task<List<WhatsAppMessage>> InboundOfAsync(WhatsAppContact contact) =>
        factory.ExecuteDbContextAsync(db => db.WhatsAppMessages
            .AsNoTracking()
            .Where(message => message.ContactId == contact.Id && message.Direction == WhatsAppMessageDirection.Inbound)
            .ToListAsync(Ct));

    /// <summary>La cola de salida corre en segundo plano: el mensaje aparece en la base cuando termina de mandarlo.</summary>
    private async Task<WhatsAppMessage> WaitForMessageAsync(string waMessageId)
    {
        WhatsAppMessage? message = null;

        await WaitUntilAsync(async () =>
        {
            message = await factory.ExecuteDbContextAsync(db =>
                db.WhatsAppMessages.AsNoTracking().SingleOrDefaultAsync(saved => saved.WaMessageId == waMessageId, Ct));

            return message is not null;
        });

        return message!;
    }

    private static Task WaitUntilAsync(Func<bool> condition) => WaitUntilAsync(() => Task.FromResult(condition()));

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!await condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    /// <summary>La cola llena para un número, mientras <see cref="Rejecting"/> sea verdadero; lo demás va a la de los tests.</summary>
    private sealed class RejectingOutbox(CapturingWhatsAppOutbox inner, PhoneNumber rejected) : IWhatsAppOutbox
    {
        public bool Rejecting { get; set; } = true;

        public bool TryEnqueue(WhatsAppOutboundMessage message) =>
            !(Rejecting && message.To == rejected) && inner.TryEnqueue(message);
    }

    /// <summary>Una persona distinta por test, que le escribe al bot desde su número, con su BSUID y su nombre de perfil.</summary>
    private sealed record Person(string WaId, string Bsuid)
    {
        public const string ProfileName = "Ana Pérez";

        public PhoneNumber Phone => PhoneNumber.Create("+" + WaId).Value;

        public static Person Unique() => new(MetaWebhook.UniqueWaId(), MetaWebhook.UniqueBsuid());

        public byte[] Says(string text, DateTime atUtc) =>
            Webhook(MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), WaId, Bsuid, atUtc, text));

        public byte[] Taps(string id, string title, DateTime atUtc) =>
            Webhook(MetaWebhook.ButtonReply(MetaWebhook.UniqueWaMessageId(), WaId, Bsuid, atUtc, id, title));

        private byte[] Webhook(JsonObject message) =>
            MetaWebhook.Build(contacts: [MetaWebhook.Contact(WaId, Bsuid, ProfileName)], messages: [message]);
    }
}
