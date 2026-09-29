using System.Net;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// La parte del módulo WhatsApp de UserInvitationEndpointsTests: la invitación por WhatsApp, la plantilla con «Quiero
/// entrar», que le pide el enlace al bot, y su estado de entrega. Los mensajes quedan en <c>factory.WhatsApp</c>: nada
/// sale hacia Meta. Lleva el namespace de la clase del núcleo, no el de su carpeta, para ser otra parte de la misma clase
/// y usar sus helpers.
/// </summary>
public sealed partial class UserInvitationEndpointsTests
{
    private const string ConsentRequiredText = "Confirmá que la persona aceptó recibir mensajes por WhatsApp.";

    private const string NameRequiredText = "Para invitar por WhatsApp, cargá el nombre de la persona.";

    private DateTime Now => factory.Clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task Inviting_by_whatsapp_without_the_consent_is_rejected_on_its_field_and_creates_nothing()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();

        using var response = await admin.CreateAsync(
            WithPhone(phone, "Laura Ríos", new { channel = "WhatsApp", consent = false }), "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(UserInvitationErrors.ConsentRequiredCode, problem.GetProperty("code").GetString());
        Assert.Equal(ConsentRequiredText, problem.GetProperty("detail").GetString());
        Assert.Equal(ConsentRequiredText, FieldError(problem, "invitation.consent"));
        Assert.Empty(factory.WhatsApp.SentTo(phone));
        Assert.False(await ExistsAsync(phone));
    }

    [Fact]
    public async Task Inviting_by_whatsapp_without_a_name_is_rejected_on_the_name_field()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();

        using var response = await admin.CreateAsync(
            WithPhone(phone, displayName: " ", new { channel = "WhatsApp", consent = true }), "es");
        var problem = await response.ReadJsonAsync();

        // La plantilla saluda por el nombre y Meta no acepta un parámetro vacío.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(UserInvitationErrors.NameRequiredCode, problem.GetProperty("code").GetString());
        Assert.Equal(NameRequiredText, FieldError(problem, "displayName"));
        Assert.False(await ExistsAsync(phone));
    }

    [Fact]
    public async Task Inviting_by_whatsapp_without_a_phone_or_by_email_without_an_email_is_rejected_on_the_channel()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);

        using var whatsApp = await admin.CreateAsync(
            new { email = TestEmails.Unique("sinnumero"), displayName = "Ana", invitation = new { channel = "WhatsApp", consent = true } },
            "es");
        using var email = await admin.CreateAsync(
            new { phone = AdminUsersApi.PhoneField(TestPhones.Unique()), invitation = new { channel = "Email" } }, "es");

        Assert.Equal("Cargá un número de WhatsApp para usar esta opción.", await ValidationMessageAsync(whatsApp, "invitation.channel"));
        Assert.Equal("Cargá un correo para usar esta opción.", await ValidationMessageAsync(email, "invitation.channel"));
    }

    [Fact]
    public async Task Inviting_by_whatsapp_queues_the_template_in_the_language_of_the_account_and_records_the_consent()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();

        // La cuenta nueva toma el idioma de la petición: la invitación sale en ese.
        var userId = await admin.CreateOkAsync(WithPhone(phone, "Laura Ríos", new { channel = "WhatsApp", consent = true }), "en");

        var message = Assert.IsType<WhatsAppInvitationMessage>(Assert.Single(factory.WhatsApp.SentTo(phone)));
        Assert.Equal("Laura Ríos", message.Name);
        Assert.Equal("Arquitectura Base", message.AppName);
        Assert.Equal("en", message.LanguageCode);
        Assert.Equal("WANT_TO_ENTER", message.ReplyPayload);
        Assert.Equal(userId, message.UserId);
        Assert.Equal("[invitación]", message.SafeSummary);

        var invitation = Assert.Single(await InvitationsOfAsync(userId));
        Assert.Equal(message.InvitationId, invitation.Id);
        Assert.Equal(UserInvitationChannel.WhatsApp, invitation.Channel);
        Assert.Equal(admin.AdminId, invitation.SentBy);
        Assert.Equal(Now, invitation.SentAtUtc);
        Assert.Equal(admin.AdminId, invitation.ConsentConfirmedBy);
        Assert.Equal(Now, invitation.ConsentConfirmedAtUtc);
        Assert.False(invitation.SendFailed);

        // Hasta que la cola lo mande y Meta avise, la invitación está pendiente.
        var last = (await admin.DetailAsync(userId)).GetProperty("lastInvitation");
        Assert.Equal("WhatsApp", last.GetProperty("channel").GetString());
        Assert.Equal(Now, last.GetProperty("sentAtUtc").GetDateTime().ToUniversalTime());
        Assert.Equal("Pending", last.GetProperty("deliveryStatus").GetString());
    }

    [Fact]
    public async Task If_the_queue_does_not_take_the_invitation_the_account_stays_and_the_invitation_shows_as_failed()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWhatsAppSendQueue>();
            services.AddSingleton<IWhatsAppSendQueue, FullSendQueue>();
        }));
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();

        var userId = await admin.CreateOkAsync(WithPhone(phone, "Laura Ríos", new { channel = "WhatsApp", consent = true }));

        Assert.Equal(phone.Value, (await admin.AccountAsync(userId)).PhoneNumber);
        Assert.True(Assert.Single(await InvitationsOfAsync(userId)).SendFailed);
        Assert.Equal("Failed", (await admin.DetailAsync(userId)).GetProperty("lastInvitation").GetProperty("deliveryStatus").GetString());
    }

    /// <summary>
    /// Con el módulo y WhatsApp apagado, el rechazo en el canal (el test del núcleo
    /// <c>With_whatsapp_off_an_invitation_by_whatsapp_is_rejected_on_the_channel_and_nothing_is_saved</c>) tampoco encola
    /// la plantilla, ni en el alta ni en el reenvío.
    /// </summary>
    [Fact]
    public async Task With_whatsapp_off_an_invitation_by_whatsapp_queues_no_template()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:PhoneNumberId", ""));
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var (phone, existingPhone) = (TestPhones.Unique(), TestPhones.Unique());
        var existing = await admin.CreateOkAsync(new { phone = AdminUsersApi.PhoneField(existingPhone), displayName = "Ana" });

        using var create = await admin.CreateAsync(
            WithPhone(phone, "Laura Ríos", new { channel = "WhatsApp", consent = true }), "es");
        using var resend = await admin.InviteAsync(existing, new { channel = "WhatsApp", consent = true }, "es");

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, resend.StatusCode);
        Assert.Empty(factory.WhatsApp.SentTo(phone));
        Assert.Empty(factory.WhatsApp.SentTo(existingPhone));
    }

    [Fact]
    public async Task Resending_by_whatsapp_goes_in_the_language_of_the_account_and_not_in_the_one_of_the_administrator()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();
        var userId = await admin.CreateOkAsync(new { phone = AdminUsersApi.PhoneField(phone), displayName = "Laura Ríos" }, "es");

        using var response = await admin.InviteAsync(userId, new { channel = "WhatsApp", consent = true }, "en");

        // La plantilla se aprueba por idioma: la de la cuenta es la que la persona lee.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var message = Assert.IsType<WhatsAppInvitationMessage>(Assert.Single(factory.WhatsApp.SentTo(phone)));
        Assert.Equal("es", message.LanguageCode);
    }

    [Fact]
    public async Task Resending_works_and_waits_a_minute_between_invitations_to_the_same_account()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();
        var userId = await admin.CreateOkAsync(WithPhone(phone, "Laura Ríos", new { channel = "WhatsApp", consent = true }));

        using var tooSoon = await admin.InviteAsync(userId, new { channel = "WhatsApp", consent = true }, "es");
        var problem = await tooSoon.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
        Assert.Equal(UserInvitationErrors.TooManyRequestsCode, problem.GetProperty("code").GetString());
        Assert.Equal("Esperá un minuto antes de volver a invitar a esta persona.", problem.GetProperty("detail").GetString());
        Assert.Equal(60, problem.GetProperty("retryAfter").GetInt32());
        Assert.Single(factory.WhatsApp.SentTo(phone));

        factory.Clock.Advance(TimeSpan.FromSeconds(60));

        using var resend = await admin.InviteAsync(userId, new { channel = "WhatsApp", consent = true });

        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        Assert.Equal(2, factory.WhatsApp.SentTo(phone).OfType<WhatsAppInvitationMessage>().Count());
        Assert.Equal(2, (await InvitationsOfAsync(userId)).Count);
    }

    [Fact]
    public async Task Resending_by_whatsapp_follows_the_same_rules_as_the_first_invitation()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var withoutName = await admin.CreateOkAsync(new { phone = AdminUsersApi.PhoneField(TestPhones.Unique()) });
        var withoutPhone = await admin.CreateOkAsync(new { email = TestEmails.Unique("solocorreo"), displayName = "Ana" });

        using var noConsent = await admin.InviteAsync(withoutName, new { channel = "WhatsApp", consent = false }, "es");
        using var noName = await admin.InviteAsync(withoutName, new { channel = "WhatsApp", consent = true }, "es");
        using var noPhone = await admin.InviteAsync(withoutPhone, new { channel = "WhatsApp", consent = true }, "es");
        var noConsentProblem = await noConsent.ReadJsonAsync();
        var noNameProblem = await noName.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);
        Assert.Equal(UserInvitationErrors.ConsentRequiredCode, noConsentProblem.GetProperty("code").GetString());
        Assert.Equal(ConsentRequiredText, FieldError(noConsentProblem, "consent"));
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        Assert.Equal(UserInvitationErrors.NameRequiredCode, noNameProblem.GetProperty("code").GetString());
        Assert.Equal("Cargá un número de WhatsApp para usar esta opción.", await ValidationMessageAsync(noPhone, "channel"));
        Assert.Empty(await InvitationsOfAsync(withoutName));
        Assert.Empty(await InvitationsOfAsync(withoutPhone));
    }

    [Fact]
    public async Task The_sender_fills_the_meta_id_of_the_invitation_and_the_detail_shows_the_status_that_meta_sends()
    {
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var meta = new FakeMetaHandler(_ => FakeMetaHandler.Success(waMessageId));
        await using var api = WithRealQueue(meta);
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();

        var userId = await admin.CreateOkAsync(WithPhone(phone, "Laura Ríos", new { channel = "WhatsApp", consent = true }));
        await WaitUntilAsync(async () => Assert.Single(await InvitationsOfAsync(userId)).ProviderMessageId == waMessageId);

        // Lo que salió hacia Meta: la plantilla de la invitación, con el nombre, el sistema y el botón.
        var template = JsonNode.Parse(Assert.Single(meta.Requests).Body!)!["template"]!;
        Assert.Equal("invitacion_acceso", template["name"]!.GetValue<string>());
        Assert.Equal("es", template["language"]!["code"]!.GetValue<string>());
        Assert.Equal("Pending", await DeliveryStatusAsync(admin, userId));

        await BotConversation.PostStatusAsync(client, waMessageId, "delivered", MetaWebhook.TruncatedToSeconds(Now), phone);

        Assert.Equal("Delivered", await DeliveryStatusAsync(admin, userId));
    }

    /// <summary>
    /// Cada aviso de Meta sobre la invitación se ve en el detalle. El que más importa es un "failed" sobre un mensaje que
    /// Meta ya había aceptado: la plantilla es MARKETING, y Meta limita cuántas de esas recibe cada persona (131049). Esa
    /// invitación no llegó, y el admin lo tiene que ver en lugar de un "pendiente" para siempre.
    /// </summary>
    [Theory]
    [InlineData("sent", null, "Sent")]
    [InlineData("read", null, "Read")]
    [InlineData("failed", 131049, "Failed")]
    public async Task The_detail_shows_each_status_that_meta_sends_for_the_invitation(string status, int? errorCode, string expected)
    {
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var meta = new FakeMetaHandler(_ => FakeMetaHandler.Success(waMessageId));
        await using var api = WithRealQueue(meta);
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();

        var userId = await admin.CreateOkAsync(WithPhone(phone, "Laura Ríos", new { channel = "WhatsApp", consent = true }));
        await WaitUntilAsync(async () => Assert.Single(await InvitationsOfAsync(userId)).ProviderMessageId == waMessageId);

        await BotConversation.PostStatusAsync(client, waMessageId, status, MetaWebhook.TruncatedToSeconds(Now), phone, errorCode);

        Assert.Equal(expected, await DeliveryStatusAsync(admin, userId));
    }

    [Fact]
    public async Task An_invitation_that_meta_rejects_shows_as_failed()
    {
        // 132001: la plantilla no existe en ese idioma, o todavía no la aprobaron. Reintentar daría lo mismo.
        var meta = new FakeMetaHandler(_ => FakeMetaHandler.Error(HttpStatusCode.NotFound, 132001));
        await using var api = WithRealQueue(meta);
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);

        var userId = await admin.CreateOkAsync(
            WithPhone(TestPhones.Unique(), "Laura Ríos", new { channel = "WhatsApp", consent = true }));
        await WaitUntilAsync(async () => Assert.Single(await InvitationsOfAsync(userId)).SendFailed);

        Assert.Equal("Failed", await DeliveryStatusAsync(admin, userId));
    }

    [Fact]
    public async Task Tapping_want_to_enter_in_the_invitation_sends_the_link_and_verifies_the_number()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();
        var userId = await admin.CreateOkAsync(WithPhone(phone, "Laura Ríos", new { channel = "WhatsApp", consent = true }));

        var bsuid = await BotConversation.TapWantToEnterAsync(factory, client, phone);

        // Fila 7 del bot: igual que una cuenta activa. El enlace nace recién ahora, no viajó en la invitación.
        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(factory.WhatsApp.SentTo(phone)[^1]);
        Assert.Equal("Entrar", reply.ButtonText);
        using var preview = await client.PostJsonAsync(
            "/account/login-link/preview", new { token = BotConversation.TokenOf(reply.Url) }, language: "es");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("Laura Ríos", (await preview.ReadJsonAsync()).GetProperty("displayName").GetString());

        Assert.True((await admin.AccountAsync(userId)).PhoneNumberConfirmed);
        Assert.Equal(userId, (await BotConversation.ContactAsync(factory, bsuid)).UserId);
    }

    /// <summary>
    /// Ningún log de la administración lleva un número entero: ni el alta con la invitación, ni la cola que la manda, ni
    /// el cambio de número, ni desvincular. Sin la invitación ni la cola, lo prueba en el núcleo
    /// No_log_of_the_phone_change_or_the_unlink_carries_the_full_number.
    /// </summary>
    [Fact]
    public async Task No_log_carries_the_full_number()
    {
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var meta = new FakeMetaHandler(_ => FakeMetaHandler.Success(waMessageId));
        await using var api = WithRealQueue(meta, services => services.AddLogging(logging => logging
            .AddFakeLogging()
            .AddFilter<FakeLoggerProvider>(category: null, LogLevel.Trace)));
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var (phone, otherPhone) = (TestPhones.Unique(), TestPhones.Unique());

        var userId = await admin.CreateOkAsync(WithPhone(phone, "Laura Ríos", new { channel = "WhatsApp", consent = true }));
        await WaitUntilAsync(async () => Assert.Single(await InvitationsOfAsync(userId)).ProviderMessageId == waMessageId);
        using var update = await admin.UpdateAsync(userId, new { roles = new[] { SystemRoles.User }, phone = AdminUsersApi.PhoneField(otherPhone) });
        using var unlink = await admin.UnlinkPhoneAsync(userId);

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);

        var logged = LoggedTexts(api.Services);

        Assert.Contains(logged, text => text.Contains(nameof(WhatsAppInvitationMessage), StringComparison.Ordinal));
        AssertNoLeaks(logged, phone, otherPhone);
    }

    private static async Task<string?> DeliveryStatusAsync(AdminUsersApi admin, Guid userId) =>
        (await admin.DetailAsync(userId)).GetProperty("lastInvitation").GetProperty("deliveryStatus").GetString();

    /// <summary>Una Api con la cola de verdad y la Graph API de mentira: el mensaje sale, y el sender lo guarda.</summary>
    private WebApplicationFactory<Program> WithRealQueue(FakeMetaHandler meta, Action<IServiceCollection>? more = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(WhatsAppInfrastructureRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => meta);
            services.RemoveAll<IWhatsAppSendQueue>();
            services.AddSingleton<IWhatsAppSendQueue>(serviceProvider => serviceProvider.GetRequiredService<WhatsAppSendQueue>());
            more?.Invoke(services);
        }));

    /// <summary>La cola corre en segundo plano: lo que guarda aparece en la base cuando termina.</summary>
    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!await condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    /// <summary>La cola llena: no toma nada.</summary>
    private sealed class FullSendQueue : IWhatsAppSendQueue
    {
        public bool TryEnqueue(WhatsAppOutboundMessage message) => false;
    }
}
