using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// Las invitaciones (secciones 6.6 y 12 del spec del ingreso con WhatsApp): el alta puede mandar una, por correo o por
/// WhatsApp, y el admin la puede reenviar. La invitación no lleva nada que sirva para entrar: por correo, un botón a
/// /login; por WhatsApp, la plantilla con «Quiero entrar», que le pide el enlace al bot. Los mensajes quedan en
/// <c>factory.WhatsApp</c> y <c>factory.EmailSender</c>: nada sale hacia Meta ni a un servidor de correo.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class UserInvitationEndpointsTests(ApiFactory factory)
{
    private const string ConsentRequiredText = "Confirmá que la persona aceptó recibir mensajes por WhatsApp.";
    private const string NameRequiredText = "Para invitar por WhatsApp, cargá el nombre de la persona.";
    private const string WhatsAppUnavailableText = "WhatsApp no está disponible en este sistema.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
    public async Task An_invitation_without_a_channel_is_rejected_on_the_channel()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);

        using var response = await admin.CreateAsync(
            new { email = TestEmails.Unique("sincanal"), invitation = new { consent = true } }, "es");

        Assert.Equal("Este campo es obligatorio.", await ValidationMessageAsync(response, "invitation.channel"));
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
            services.RemoveAll<IWhatsAppOutbox>();
            services.AddSingleton<IWhatsAppOutbox, FullOutbox>();
        }));
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();

        var userId = await admin.CreateOkAsync(WithPhone(phone, "Laura Ríos", new { channel = "WhatsApp", consent = true }));

        Assert.Equal(phone.Value, (await admin.AccountAsync(userId)).PhoneNumber);
        Assert.True(Assert.Single(await InvitationsOfAsync(userId)).SendFailed);
        Assert.Equal("Failed", (await admin.DetailAsync(userId)).GetProperty("lastInvitation").GetProperty("deliveryStatus").GetString());
    }

    [Fact]
    public async Task Inviting_by_email_sends_the_new_email_with_the_button_to_the_login_in_the_language_of_the_account()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var email = TestEmails.Unique("invitada");

        var userId = await admin.CreateOkAsync(
            new { email, displayName = "Laura", invitation = new { channel = "Email" } }, "en");

        var sent = await factory.EmailSender.WaitForAsync(email);
        Assert.Equal("You've been given access to Arquitectura Base", sent.Subject);
        Assert.Contains("Hi, Laura. An administrator gave you access to Arquitectura Base.", sent.TextBody, StringComparison.Ordinal);
        Assert.Contains("href=\"https://localhost/login\"", sent.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("lang=\"en\"", sent.HtmlBody, StringComparison.Ordinal);

        // Por correo no hay estado de entrega que seguir.
        var last = (await admin.DetailAsync(userId)).GetProperty("lastInvitation");
        Assert.Equal("Email", last.GetProperty("channel").GetString());
        Assert.Equal(JsonValueKind.Null, last.GetProperty("deliveryStatus").ValueKind);

        var invitation = Assert.Single(await InvitationsOfAsync(userId));
        Assert.Equal(UserInvitationChannel.Email, invitation.Channel);
        Assert.Null(invitation.ConsentConfirmedBy);
        Assert.Null(invitation.ConsentConfirmedAtUtc);
    }

    [Fact]
    public async Task Resending_by_email_goes_in_the_language_of_the_account_and_not_in_the_one_of_the_administrator()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var email = TestEmails.Unique("reenvio");
        var userId = await admin.CreateOkAsync(new { email }, "es");

        using var response = await admin.InviteAsync(userId, new { channel = "Email" }, "en");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var sent = await factory.EmailSender.WaitForAsync(email);
        Assert.Equal("Te dieron acceso a Arquitectura Base", sent.Subject);

        // Sin nombre, el saludo no inventa uno.
        Assert.Contains("Hola. Un administrador te dio acceso a Arquitectura Base.", sent.TextBody, StringComparison.Ordinal);
        Assert.Contains("Para entrar, usá este correo: te vamos a mandar un código de acceso.", sent.TextBody, StringComparison.Ordinal);
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

    /// <summary>
    /// Sin WhatsApp configurado no hay por dónde mandar la plantilla: la invitación por WhatsApp se rechaza en el canal,
    /// antes de crear la cuenta o de guardar nada, como el ingreso, que ni ofrece la opción. Aceptarla daría un éxito y
    /// una invitación fallida sin decir por qué.
    /// </summary>
    [Fact]
    public async Task With_whatsapp_off_an_invitation_by_whatsapp_is_rejected_on_the_channel_and_nothing_is_saved()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:PhoneNumberId", ""));
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var (phone, existingPhone) = (TestPhones.Unique(), TestPhones.Unique());
        var existing = await admin.CreateOkAsync(new { phone = AdminUsersApi.PhoneField(existingPhone), displayName = "Ana" });

        using var create = await admin.CreateAsync(
            WithPhone(phone, "Laura Ríos", new { channel = "WhatsApp", consent = true }), "es");
        using var resend = await admin.InviteAsync(existing, new { channel = "WhatsApp", consent = true }, "es");

        Assert.Equal(WhatsAppUnavailableText, await ValidationMessageAsync(create, "invitation.channel"));
        Assert.Equal(WhatsAppUnavailableText, await ValidationMessageAsync(resend, "channel"));
        Assert.False(await ExistsAsync(phone));
        Assert.Empty(await InvitationsOfAsync(existing));
        Assert.Empty(factory.WhatsApp.SentTo(phone));
        Assert.Empty(factory.WhatsApp.SentTo(existingPhone));
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
    public async Task A_deactivated_account_is_not_invited_and_a_deleted_one_is_not_found()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var deactivated = await admin.CreateOkAsync(new { email = TestEmails.Unique("inactiva") });
        var deleted = await admin.CreateOkAsync(new { email = TestEmails.Unique("borrada") });
        using var off = await client.SendWithTokenAsync(HttpMethod.Post, $"/api/users/{deactivated}/deactivate", admin.AccessToken);
        using var delete = await client.SendWithTokenAsync(HttpMethod.Delete, $"/api/users/{deleted}", admin.AccessToken);

        using var toDeactivated = await admin.InviteAsync(deactivated, new { channel = "Email" }, "es");
        using var toDeleted = await admin.InviteAsync(deleted, new { channel = "Email" }, "es");
        var deactivatedProblem = await toDeactivated.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, toDeactivated.StatusCode);
        Assert.Equal(UserInvitationErrors.UserInactiveCode, deactivatedProblem.GetProperty("code").GetString());
        Assert.Equal("La cuenta está desactivada: activala antes de invitarla.", deactivatedProblem.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.NotFound, toDeleted.StatusCode);
        Assert.Equal(UserErrors.NotFoundCode, (await toDeleted.ReadJsonAsync()).GetProperty("code").GetString());
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
        await WaitUntilAsync(async () => Assert.Single(await InvitationsOfAsync(userId)).WaMessageId == waMessageId);

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
        await WaitUntilAsync(async () => Assert.Single(await InvitationsOfAsync(userId)).WaMessageId == waMessageId);

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
    /// el cambio de número, ni desvincular.
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
        await WaitUntilAsync(async () => Assert.Single(await InvitationsOfAsync(userId)).WaMessageId == waMessageId);
        using var update = await admin.UpdateAsync(userId, new { roles = new[] { SystemRoles.User }, phone = AdminUsersApi.PhoneField(otherPhone) });
        using var unlink = await admin.UnlinkPhoneAsync(userId);

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);

        var logged = api.Services.GetFakeLogCollector().GetSnapshot()
            .Select(record => string.Join(
                "\n",
                [
                    record.Message,
                    record.Exception?.ToString() ?? string.Empty,
                    .. record.StructuredState?.Select(pair => pair.Value ?? string.Empty) ?? [],
                    .. record.Scopes.Select(scope => scope?.ToString() ?? string.Empty),
                ]))
            .ToList();

        Assert.Contains(logged, text => text.Contains(nameof(WhatsAppInvitationMessage), StringComparison.Ordinal));

        string[] secrets =
        [
            phone.Value, phone.Value[1..], TestPhones.AsTypedLocally(phone),
            otherPhone.Value, otherPhone.Value[1..], TestPhones.AsTypedLocally(otherPhone),
        ];
        var leaks = secrets
            .SelectMany(secret => logged
                .Where(text => Regex.IsMatch(text, $"(?<![0-9A-Za-z]){Regex.Escape(secret)}(?![0-9A-Za-z])"))
                .Select(text => secret + " in: " + text))
            .ToList();

        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine + "---" + Environment.NewLine, leaks));
    }

    [Theory]
    [InlineData("POST", "/invitation")]
    [InlineData("DELETE", "/whatsapp")]
    public async Task Inviting_and_unlinking_require_the_users_manage_permission(string method, string route)
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sinpermiso"));

        using var response = await client.SendWithTokenAsync(
            new HttpMethod(method),
            $"/api/users/{Guid.CreateVersion7()}{route}",
            tokens.AccessToken,
            method == "POST" ? new { channel = "Email" } : null,
            language: "es");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    private static object WithPhone(PhoneNumber phone, string? displayName, object invitation) => new
    {
        phone = AdminUsersApi.PhoneField(phone),
        displayName,
        roles = new[] { SystemRoles.User },
        invitation,
    };

    private static string? FieldError(JsonElement problem, string field) =>
        problem.GetProperty("errors").GetProperty(field)[0].GetString();

    /// <summary>El primer mensaje de <paramref name="field"/> en un 400 de validación.</summary>
    private static async Task<string?> ValidationMessageAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.ReadJsonAsync();
        Assert.Equal(ValidationError.ErrorCode, problem.GetProperty("code").GetString());

        return FieldError(problem, field);
    }

    private static async Task<string?> DeliveryStatusAsync(AdminUsersApi admin, Guid userId) =>
        (await admin.DetailAsync(userId)).GetProperty("lastInvitation").GetProperty("deliveryStatus").GetString();

    private Task<bool> ExistsAsync(PhoneNumber phone) =>
        factory.ExecuteDbContextAsync(db => db.Users.IgnoreQueryFilters().AnyAsync(user => user.PhoneNumber == phone.Value, Ct));

    private Task<List<UserInvitation>> InvitationsOfAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.UserInvitations
            .AsNoTracking()
            .Where(invitation => invitation.UserId == userId)
            .OrderBy(invitation => invitation.SentAtUtc)
            .ToListAsync(Ct));

    /// <summary>Una Api con la cola de verdad y la Graph API de mentira: el mensaje sale, y el sender lo guarda.</summary>
    private WebApplicationFactory<Program> WithRealQueue(FakeMetaHandler meta, Action<IServiceCollection>? more = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(WhatsAppRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => meta);
            services.RemoveAll<IWhatsAppOutbox>();
            services.AddSingleton<IWhatsAppOutbox>(serviceProvider => serviceProvider.GetRequiredService<WhatsAppOutbox>());
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
    private sealed class FullOutbox : IWhatsAppOutbox
    {
        public bool TryEnqueue(WhatsAppOutboundMessage message) => false;
    }
}
