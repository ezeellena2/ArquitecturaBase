using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Emails;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// Las invitaciones (secciones 6.6 y 12 del spec del ingreso con WhatsApp): el alta puede mandar una y el admin la puede
/// reenviar. La invitación no lleva nada que sirva para entrar: por correo, un botón a /login. Los correos quedan en
/// <c>factory.EmailSender</c>: nada sale a un servidor de correo. Las invitaciones por WhatsApp están en la parte del
/// módulo de esta clase (su carpeta <c>Modules/WhatsApp</c>); sin él, invitar por WhatsApp se rechaza en el canal.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed partial class UserInvitationEndpointsTests(ApiFactory factory)
{
    private const string WhatsAppUnavailableText = "WhatsApp no está disponible en este sistema.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_invitation_without_a_channel_is_rejected_on_the_channel()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);

        using var response = await admin.CreateAsync(
            new { email = TestEmails.Unique("sincanal"), invitation = new { consent = true } }, "es");

        Assert.Equal("Este campo es obligatorio.", await ValidationMessageAsync(response, "invitation.channel"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resending_requires_a_supported_channel_before_taking_the_account_lock(bool unsupported)
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var userId = await admin.CreateOkAsync(new { email = TestEmails.Unique("resend-invalid-channel") });

        object request = unsupported ? new { channel = 999 } : new { consent = false };
        using var response = await admin.InviteAsync(userId, request, "es");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            unsupported ? "Elegí por dónde mandar la invitación." : "Este campo es obligatorio.",
            await ValidationMessageAsync(response, "channel"));
        Assert.Empty(await InvitationsOfAsync(userId));
    }

    [Fact]
    public async Task If_the_email_queue_does_not_take_the_invitation_it_shows_as_failed_and_the_resend_does_not_wait()
    {
        var emailQueue = new SwitchableEmailQueue();
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailQueue>();
            services.AddSingleton<IEmailQueue>(provider => emailQueue.Over(provider.GetRequiredService<EmailQueue>()));
        }));
        using var client = api.CreateClient();
        // El código del administrador tiene que llegar: la cola se cierra recién para la invitación.
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var email = TestEmails.Unique("cola-llena");
        emailQueue.Accepts = false;
        emailQueue.Queued.Clear();

        var userId = await admin.CreateOkAsync(new { email, displayName = "Laura", invitation = new { channel = "Email" } });

        Assert.True(Assert.Single(await InvitationsOfAsync(userId)).SendFailed);
        var failed = (await admin.DetailAsync(userId)).GetProperty("lastInvitation");
        Assert.Equal("Email", failed.GetProperty("channel").GetString());
        Assert.Equal("Failed", failed.GetProperty("deliveryStatus").GetString());

        emailQueue.Accepts = true;
        using var resend = await admin.InviteAsync(userId, new { channel = "Email" });

        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        Assert.Equal(email, Assert.Single(emailQueue.Queued).To);
        Assert.Equal([true, false], (await InvitationsOfAsync(userId)).Select(invitation => invitation.SendFailed));
        Assert.Equal(JsonValueKind.Null, (await admin.DetailAsync(userId)).GetProperty("lastInvitation").GetProperty("deliveryStatus").ValueKind);
    }

    [Fact]
    public async Task Inviting_by_email_sends_the_new_email_with_the_button_to_the_login_in_the_language_of_the_account()
    {
        await using var defaults = await SystemCultureOverride.SetAsync(factory, "en");
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var email = TestEmails.Unique("invitada");

        var userId = await admin.CreateOkAsync(
            new { email, displayName = "Laura", invitation = new { channel = "Email" } }, "en");

        var sent = await factory.EmailSender.WaitForAsync(email);
        Assert.Equal("You've been given access to Acceso", sent.Subject);
        Assert.Contains("Hi, Laura. An administrator gave you access to Acceso.", sent.TextBody, StringComparison.Ordinal);
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
        Assert.Equal("Te dieron acceso a Acceso", sent.Subject);

        // Sin nombre, el saludo no inventa uno.
        Assert.Contains("Hola. Un administrador te dio acceso a Acceso.", sent.TextBody, StringComparison.Ordinal);
        Assert.Contains("Para entrar, usá este correo: te vamos a mandar un código de acceso.", sent.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Sin WhatsApp configurado no hay por dónde mandar la plantilla: la invitación por WhatsApp se rechaza en el canal,
    /// antes de crear la cuenta o de guardar nada, como el ingreso, que ni ofrece la opción. Aceptarla daría un éxito y
    /// una invitación fallida sin decir por qué. Sin el módulo pasa lo mismo: no hay canal de WhatsApp, y
    /// UserInvitationIssuer responde el mismo error en el mismo campo. Que tampoco se encola la plantilla lo prueba la
    /// parte del módulo.
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
    public async Task Inviting_by_email_without_an_email_is_rejected_on_the_channel()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);

        using var email = await admin.CreateAsync(
            new { phone = AdminUsersApi.PhoneField(TestPhones.Unique()), invitation = new { channel = "Email" } }, "es");

        Assert.Equal("Cargá un correo para usar esta opción.", await ValidationMessageAsync(email, "invitation.channel"));
    }

    [Fact]
    public async Task Resending_by_email_works_and_waits_a_minute_between_invitations_to_the_same_account()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var email = TestEmails.Unique("espera");
        var userId = await admin.CreateOkAsync(new { email, displayName = "Laura Ríos", invitation = new { channel = "Email" } });

        using var tooSoon = await admin.InviteAsync(userId, new { channel = "Email" }, "es");
        var problem = await tooSoon.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
        Assert.Equal(UserInvitationErrors.TooManyRequestsCode, problem.GetProperty("code").GetString());
        Assert.Equal("Esperá un minuto antes de volver a invitar a esta persona.", problem.GetProperty("detail").GetString());
        Assert.Equal(60, problem.GetProperty("retryAfter").GetInt32());
        Assert.Single(await InvitationsOfAsync(userId));

        factory.Clock.Advance(TimeSpan.FromSeconds(60));

        using var resend = await admin.InviteAsync(userId, new { channel = "Email" });

        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        Assert.Equal(2, (await InvitationsOfAsync(userId)).Count);
        await factory.EmailSender.WaitForAsync(email, number: 2);
    }

    /// <summary>
    /// Ningún log del cambio de número ni del desvincular desde la administración lleva un número entero. Con la
    /// invitación por WhatsApp y la cola que la manda, lo prueba la parte del módulo (No_log_carries_the_full_number).
    /// </summary>
    [Fact]
    public async Task No_log_of_the_phone_change_or_the_unlink_carries_the_full_number()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddLogging(logging => logging.AddFakeLogging().AddFilter<FakeLoggerProvider>(category: null, LogLevel.Trace))));
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var (phone, otherPhone) = (TestPhones.Unique(), TestPhones.Unique());

        var userId = await admin.CreateOkAsync(new { phone = AdminUsersApi.PhoneField(phone), displayName = "Laura Ríos" });
        using var update = await admin.UpdateAsync(userId, new { roles = new[] { SystemRoles.User }, phone = AdminUsersApi.PhoneField(otherPhone) });
        using var unlink = await admin.UnlinkPhoneAsync(userId);

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);

        var logged = LoggedTexts(api.Services);

        // Que el detector mire algo: las dos operaciones dejaron su log.
        Assert.Contains(logged, text => text.Contains("Handled UpdateUser", StringComparison.Ordinal));
        Assert.Contains(logged, text => text.Contains("Handled UnlinkUserPhone", StringComparison.Ordinal));
        AssertNoLeaks(logged, phone, otherPhone);
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

    /// <summary>Todo lo que quedó en los logs: el mensaje, la excepción, los valores estructurados y los scopes.</summary>
    private static List<string> LoggedTexts(IServiceProvider services) =>
        [
            .. services.GetFakeLogCollector().GetSnapshot()
                .Select(record => string.Join(
                    "\n",
                    [
                        record.Message,
                        record.Exception?.ToString() ?? string.Empty,
                        .. record.StructuredState?.Select(pair => pair.Value ?? string.Empty) ?? [],
                        .. record.Scopes.Select(scope => scope?.ToString() ?? string.Empty),
                    ])),
        ];

    /// <summary>Ningún texto lleva el número entero, sin el "+" o como lo tipea una persona.</summary>
    private static void AssertNoLeaks(List<string> logged, params PhoneNumber[] phones)
    {
        var leaks = phones
            .SelectMany(phone => new[] { phone.Value, phone.Value[1..], TestPhones.AsTypedLocally(phone) })
            .SelectMany(secret => logged
                .Where(text => Regex.IsMatch(text, $"(?<![0-9A-Za-z]){Regex.Escape(secret)}(?![0-9A-Za-z])"))
                .Select(text => secret + " in: " + text))
            .ToList();

        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine + "---" + Environment.NewLine, leaks));
    }

    private Task<bool> ExistsAsync(PhoneNumber phone) =>
        factory.ExecuteDbContextAsync(db => db.Users.IgnoreQueryFilters().AnyAsync(user => user.PhoneNumber == phone.Value, Ct));

    private Task<List<UserInvitation>> InvitationsOfAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.UserInvitations
            .AsNoTracking()
            .Where(invitation => invitation.UserId == userId)
            .OrderBy(invitation => invitation.SentAtUtc)
            .ToListAsync(Ct));
}
