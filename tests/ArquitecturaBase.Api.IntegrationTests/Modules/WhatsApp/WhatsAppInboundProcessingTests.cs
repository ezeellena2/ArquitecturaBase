using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// El ingreso de mensajes de punta a punta: un webhook firmado como los de Meta,
/// después una vuelta del procesador (<see cref="WhatsAppInboundProcessor.ProcessPendingAsync"/>) y después lo que se
/// encoló para mandar (<c>factory.WhatsApp</c>). En el arnés el procesador no corre solo: cada test lo llama cuando
/// quiere. Todos los tests comparten la base, así que cada uno escribe desde un número propio y mira solo lo suyo.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppInboundProcessingTests(ApiFactory factory)
{
    private const string Route = "/webhooks/whatsapp";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime Now => MetaWebhook.TruncatedToSeconds(factory.Clock.GetUtcNow().UtcDateTime);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Business_messages_never_create_confirm_link_or_issue_access(bool hasAccount)
    {
        var person = Person.Unique();
        var account = hasAccount ? await CreateAccountAsync(person.Phone, "Ana Pérez", phoneConfirmed: false) : null;
        using var client = factory.CreateClient();
        using var response = await PostSignedAsync(client, person.Says("Hola, quiero entrar CREATE_ACCOUNT", Now));
        await ProcessPendingAsync();
        Assert.Empty(factory.WhatsApp.SentTo(person.Phone));
        var contact = await FindContactAsync(person);
        Assert.Equal(WhatsAppConversationMode.Manual, contact.Mode);
        Assert.Null(contact.UserId);
        if (account is not null)
        {
            Assert.False((await FindAccountAsync(account.Id)).PhoneNumberConfirmed);
            Assert.False(await factory.ExecuteDbContextAsync(db => db.LoginLinks.AnyAsync(link => link.UserId == account.Id, Ct)));
        }
        else
        {
            Assert.Null(await FindAccountByPhoneAsync(person.Phone));
        }
        var inbound = Assert.Single(await InboundOfAsync(contact));
        Assert.Equal(WhatsAppMessagePurpose.Business, inbound.Purpose);
        Assert.Null(inbound.AccessEventKind);
        Assert.NotNull(inbound.ProcessedAtUtc);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("WANT_TO_ENTER", "login")]
    [InlineData("HAVE_ACCOUNT", "login")]
    [InlineData("CREATE_ACCOUNT", "registro")]
    public async Task Existing_buttons_in_manual_offer_public_web_instructions_without_identity_mutations(string action, string path)
    {
        var person = Person.Unique();
        using var client = factory.CreateClient();
        using var response = await PostSignedAsync(client, person.Taps(action, "Botón ya enviado", Now));
        await ProcessPendingAsync();
        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(factory.WhatsApp.SentTo(person.Phone)));
        Assert.Equal("https://localhost/" + path, reply.Url);
        Assert.Equal(WhatsAppMessagePurpose.Access, reply.Purpose);
        Assert.Equal(WhatsAppAccessEventKind.AccessInstructions, reply.AccessEventKind);
        Assert.Null(await FindAccountByPhoneAsync(person.Phone));
        Assert.Null((await FindContactAsync(person)).UserId);
        Assert.Equal(WhatsAppMessagePurpose.Access, Assert.Single(await InboundOfAsync(await FindContactAsync(person))).Purpose);
    }

    /// <summary>Meta reintenta el webhook que no recibió su 200: el mensaje repetido no recibe otra respuesta.</summary>
    [Fact]
    public async Task A_repeated_webhook_gets_a_single_reply()
    {
        var person = Person.Unique();
        await CreateAccountAsync(person.Phone, "Ana Pérez", phoneConfirmed: true);
        var body = person.Taps(WhatsAppAccessActions.WantToEnter, "Quiero entrar", Now);
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

        using var first = await PostSignedAsync(client, person.Taps(WhatsAppAccessActions.WantToEnter, "Quiero entrar", Now.AddSeconds(-2)));
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

    [Fact]
    public async Task A_new_bsuid_does_not_take_over_an_existing_account_link()
    {
        var before = Person.Unique();
        var newcomer = before with { Bsuid = MetaWebhook.UniqueBsuid() };
        var account = await CreateAccountAsync(before.Phone, "Ana", phoneConfirmed: true);
        using var client = factory.CreateClient();
        using var first = await PostSignedAsync(client, before.Says("Hola", Now));
        await factory.InTransactionAsync(async services =>
        {
            var repository = services.GetRequiredService<IWhatsAppContactRepository>();
            var contact = await repository.GetByUserIdentifierAsync(before.Bsuid, Ct);
            contact!.LinkUser(account.Id);
        });
        using var second = await PostSignedAsync(client, newcomer.Says("Hola de nuevo", Now));
        await ProcessPendingAsync();
        Assert.Equal(account.Id, (await FindContactAsync(before)).UserId);
        Assert.Null((await FindContactAsync(newcomer)).UserId);
        Assert.Empty(factory.WhatsApp.SentTo(before.Phone));
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
            {
                services.AddSingleton<ArquitecturaBase.Infrastructure.Delivery.IMessageSource<ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppOutboundMessage>, WhatsAppMessageSource>();
                services.AddHttpClient(WhatsAppInfrastructureRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => meta);
            }));
        using var client = api.CreateClient();
        var person = Person.Unique();
        using var hello = await PostSignedAsync(client, person.Says("Hola", Now.AddSeconds(-5)));
        var contact = await FindContactAsync(person);
        var message = new WhatsAppLinkButtonMessage(
            person.Phone,
            "Hola, Ana Pérez. Para entrar a Acceso, tocá Entrar.",
            "Entrar",
            "https://localhost/ingresar#t=secret-token",
            "Por ahora este chat solo sirve para entrar.");

        await using (var scope = api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ArquitecturaBase.Application.Interfaces.Persistence.IUnitOfWork>().ExecuteInTransactionAsync(_ =>
            {
                Assert.True(scope.ServiceProvider.GetRequiredService<WhatsAppSendQueue>().TryEnqueue(message));
                return Task.FromResult(ArquitecturaBase.Domain.Results.Result.Success());
            }, ArquitecturaBase.Application.Interfaces.Persistence.CommitPolicy.OnSuccess, Ct);
        }
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
        var sendQueue = new RejectingSendQueue(factory.WhatsApp, failing.Phone);
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWhatsAppSendQueue>();
            services.AddSingleton<IWhatsAppSendQueue>(sendQueue);
        }));
        using var client = api.CreateClient();
        var processor = api.Services.GetRequiredService<WhatsAppInboundProcessor>();

        // El que falla escribió primero: el procesador lo intenta antes que al otro.
        using var first = await PostSignedAsync(client, failing.Taps(WhatsAppAccessActions.CreateAccount, "Crear cuenta", Now.AddSeconds(-1)));
        using var second = await PostSignedAsync(client, other.Taps(WhatsAppAccessActions.WantToEnter, "Quiero entrar", Now));
        await processor.ProcessPendingAsync(Ct);

        Assert.Empty(factory.WhatsApp.SentTo(failing.Phone));
        Assert.Single(factory.WhatsApp.SentTo(other.Phone));
        Assert.Null(Assert.Single(await InboundOfAsync(await FindContactAsync(failing))).ProcessedAtUtc);
        Assert.Null(await FindAccountByPhoneAsync(failing.Phone));

        sendQueue.Rejecting = false;
        await processor.ProcessPendingAsync(Ct);

        Assert.Equal("https://localhost/registro",
            Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(factory.WhatsApp.SentTo(failing.Phone))).Url);
        Assert.Null(await FindAccountByPhoneAsync(failing.Phone));
    }

    /// <summary>
    /// Varias instancias (sección 7 del spec), con el lock de verdad de Postgres: el contacto que una tiene tomado no lo
    /// lee ninguna otra, que lo saltea sin esperar y les contesta a los demás. Mientras tanto el webhook le sigue
    /// guardando mensajes, porque el lock del bot no traba la clave foránea de un mensaje nuevo. Cuando la primera lo
    /// suelta (acá, sin confirmar nada), la próxima vuelta le contesta una sola vez a todo lo que quedó.
    /// </summary>
    [Fact]
    public async Task A_taken_contact_is_skipped_while_its_inbound_sequence_waits_and_other_contacts_keep_arriving()
    {
        var taken = Person.Unique();
        var other = Person.Unique();
        using var client = factory.CreateClient();
        using var hello = await PostSignedAsync(client, taken.Taps(WhatsAppAccessActions.WantToEnter, "Quiero entrar", Now));
        var contact = await FindContactAsync(taken);

        Task<HttpResponseMessage>? arriving = null;
        await using (var first = factory.Services.CreateAsyncScope())
        {
            // La primera instancia abre su transacción, toma el contacto y la deja abierta, como mientras decide la
            // respuesta.
            await using var firstTransaction = await first.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Database.BeginTransactionAsync(Ct);
            Assert.NotNull(await first.ServiceProvider.GetRequiredService<IWhatsAppContactRepository>()
                .GetForProcessingAsync(contact.Id, Ct));

            // Lo que sigue no espera al lock: si esperara, lo cortaría este plazo, porque el lock no se suelta hasta el
            // final del bloque.
            using var noWait = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            noWait.CancelAfter(TimeSpan.FromSeconds(10));

            await using (var second = factory.Services.CreateAsyncScope())
            {
                await using var secondTransaction = await second.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .Database.BeginTransactionAsync(noWait.Token);
                Assert.Null(await second.ServiceProvider.GetRequiredService<IWhatsAppContactRepository>()
                    .GetForProcessingAsync(contact.Id, noWait.Token));
            }

            // Every inbound now advances the contact sequence; it waits for the processing row lock.
            arriving = PostSignedWithinAsync(client, taken.Says("¿Hay alguien?", Now.AddSeconds(-10)), Ct);
            using var fromOther = await PostSignedWithinAsync(client, other.Taps(WhatsAppAccessActions.WantToEnter, "Quiero entrar", Now), noWait.Token);

            await using var otherInstance = factory.WithWebHostBuilder(_ => { });
            await otherInstance.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);

            Assert.Empty(factory.WhatsApp.SentTo(taken.Phone));
            Assert.Single(factory.WhatsApp.SentTo(other.Phone));

            var pending = await InboundOfAsync(contact);
            Assert.Single(pending);
            Assert.All(pending, message => Assert.Null(message.ProcessedAtUtc));
        }
        using var late = await arriving!;
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
        Assert.Equal(2, (await InboundOfAsync(contact)).Count);

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

        using var response = await PostSignedAsync(client, person.Taps(WhatsAppAccessActions.WantToEnter, "Quiero entrar", Now));

        await WaitUntilAsync(() => factory.WhatsApp.CountFor(person.Phone) == 1);
        Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(factory.WhatsApp.SentTo(person.Phone)));
    }

    private Task ProcessPendingAsync() =>
        factory.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);

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
        factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>()
            .CreateAsync(email: null, phone, phoneConfirmed, displayName, "es", Ct));

    private Task<UserAccount> FindAccountAsync(Guid userId) =>
        factory.ExecuteScopeAsync(async services =>
            Assert.IsType<UserAccount>(await services.GetRequiredService<IUserReader>().FindByIdAsync(userId, Ct)));

    private Task<UserAccount?> FindAccountByPhoneAsync(PhoneNumber phone) =>
        factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserReader>().FindByPhoneAsync(phone, Ct));

    private Task<WhatsAppContact> FindContactAsync(Person person) =>
        factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>().AsNoTracking().SingleAsync(contact => contact.UserIdentifier == person.Bsuid, Ct));

    private Task<List<WhatsAppMessage>> InboundOfAsync(WhatsAppContact contact) =>
        factory.ExecuteDbContextAsync(db => db.Set<WhatsAppMessage>()
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
                db.Set<WhatsAppMessage>().AsNoTracking().SingleOrDefaultAsync(saved => saved.WaMessageId == waMessageId, Ct));

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
    private sealed class RejectingSendQueue(CapturingWhatsAppSendQueue inner, PhoneNumber rejected) : IWhatsAppSendQueue
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
