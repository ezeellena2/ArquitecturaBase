using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Common.RateLimiting;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

/// <summary>
/// N7: los topes diarios de lo que sale por correo, contados en la base: por dirección (una IPv6, por su /64), por cuenta
/// en el perfil, por administrador en las invitaciones y el de todos, con la reserva de las invitaciones y su aviso. Cada test levanta una Api con sus topes
/// y una dirección propia (el arnés no tiene IP remota: acá la pone <see cref="ClientIpStartupFilter"/>). Las reglas de cada
/// tope las prueba EmailQuotaGuardTests.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class EmailQuotaEndpointsTests(ApiFactory factory)
{
    private const string ClientIpHeader = "X-Test-Client-Ip";
    private const string LoginCodeUrl = "/account/login-code";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // En InviteOnly, a un correo sin cuenta el código no sale, pero gasta el lugar igual: la dirección llega al tope con
    // los dos y responde lo mismo para los dos. Otra dirección del mismo /64 comparte el tope; otra red, no.
    [Fact]
    public async Task An_address_at_its_daily_limit_answers_the_same_with_or_without_an_account_and_its_64_counts_together()
    {
        var network = UniqueNetwork();
        await using var api = Api(("Email:DailyCodeLimitPerAddress", "2"));
        using var client = api.CreateClient();
        await using var inviteOnly = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        var known = TestEmails.Unique("tope-con-cuenta");
        await factory.InTransactionAsync(services =>
            services.GetRequiredService<IUserRepository>().CreateUnverifiedAsync(Email.Create(known).Value, phone: null, "Ana", "es", Ct));
        var unknown = TestEmails.Unique("tope-sin-cuenta");

        using var first = await RequestCodeAsync(client, known, network + "::1");
        using var second = await RequestCodeAsync(client, unknown, network + "::2");
        using var withAccount = await RequestCodeAsync(client, known, network + ":aaaa::3");
        using var withoutAccount = await RequestCodeAsync(client, unknown, network + ":bbbb::4");
        using var otherNetwork = await RequestCodeAsync(client, unknown, UniqueNetwork() + "::1");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var withProblem = await withAccount.ReadJsonAsync();
        var withoutProblem = await withoutAccount.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.TooManyRequests, withAccount.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, withoutAccount.StatusCode);
        Assert.Equal(LoginCodeErrors.DailyLimitReachedCode, withProblem.GetProperty("code").GetString());
        Assert.Equal(LoginCodeErrors.DailyLimitReachedCode, withoutProblem.GetProperty("code").GetString());
        Assert.Equal("Por hoy no se pueden mandar más códigos. Probá de nuevo más tarde.", withProblem.GetProperty("detail").GetString());
        Assert.Equal(withProblem.GetProperty("detail").GetString(), withoutProblem.GetProperty("detail").GetString());
        Assert.InRange(withProblem.GetProperty("retryAfter").GetInt32(), 23 * 60 * 60, 24 * 60 * 60);
        Assert.InRange(withoutProblem.GetProperty("retryAfter").GetInt32(), 23 * 60 * 60, 24 * 60 * 60);
        Assert.Equal(HttpStatusCode.Accepted, otherNetwork.StatusCode);

        // Un pedido rechazado no deja código, y cada código guarda la dirección agrupada.
        var codes = await factory.ExecuteDbContextAsync(db => db.LoginCodes
            .Where(code => code.Destination == known || code.Destination == unknown)
            .OrderBy(code => code.CreatedAtUtc)
            .ToListAsync(Ct));
        Assert.Equal(3, codes.Count);
        Assert.Equal([AddressPartition.Of(network + "::1"), AddressPartition.Of(network + "::1")], codes.Take(2).Select(code => code.RequestedFromAddress));
        Assert.NotNull(codes[0].SentAtUtc);
        Assert.Null(codes[1].SentAtUtc);
    }

    // El de todos cuenta lo que salió por correo (códigos e invitaciones) por debajo del cupo de la cuenta que manda. Los
    // códigos frenan en el tope menos la reserva de las invitaciones, y las invitaciones llegan al tope: un abuso anónimo de
    // los códigos no deja a los administradores sin invitar. Avisa en el log desde el 80 % y al llegar al tope, una vez cada
    // uno.
    [Fact]
    public async Task The_limit_of_everyone_stops_codes_before_the_invitation_reserve_and_invitations_at_the_limit_and_warns()
    {
        const int reserve = 2;
        var sent = await SentByEmailInTheWindowAsync();
        var limit = Math.Max(sent + 6, 10);
        await using var api = Api(
            ("Email:DailyLimit", limit.ToString(CultureInfo.InvariantCulture)),
            ("Email:DailyInvitationReserve", reserve.ToString(CultureInfo.InvariantCulture)));
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var firstInvited = await admin.CreateOkAsync(new { email = TestEmails.Unique("tope-global-invitada-1") });
        var secondInvited = await admin.CreateOkAsync(new { email = TestEmails.Unique("tope-global-invitada-2") });

        var accepted = 0;
        HttpResponseMessage? rejected = null;
        while (rejected is null && accepted <= limit)
        {
            var response = await client.PostJsonAsync(LoginCodeUrl, new { email = TestEmails.Unique("tope-global") }, "es");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
                break;
            }

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            response.Dispose();
            accepted++;
        }

        Assert.NotNull(rejected);
        using var rejectedResponse = rejected;
        using var firstInvitation = await admin.InviteAsync(firstInvited, new { channel = "Email" }, "es");
        using var secondInvitation = await admin.InviteAsync(secondInvited, new { channel = "Email" }, "es");
        var thirdEmail = TestEmails.Unique("tope-global-invitada-3");
        using var thirdInvitation = await admin.CreateAsync(new { email = thirdEmail, invitation = new { channel = "Email" } }, "es");
        var codeProblem = await rejectedResponse.ReadJsonAsync();
        var invitationProblem = await thirdInvitation.ReadJsonAsync();

        // El ingreso del administrador gastó uno: los demás códigos, hasta el tope menos la reserva; las invitaciones, la
        // reserva, y la que sigue no deja nada guardado.
        Assert.Equal(limit - reserve - sent - 1, accepted);
        Assert.Equal(LoginCodeErrors.DailyLimitReachedCode, codeProblem.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Accepted, firstInvitation.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, secondInvitation.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, thirdInvitation.StatusCode);
        Assert.Equal(UserInvitationErrors.DailyLimitReachedCode, invitationProblem.GetProperty("code").GetString());
        Assert.Equal(
            "Por hoy no se pueden mandar más invitaciones por correo, y no se guardó nada. Si estás dando de alta a alguien, podés hacerlo sin invitación y mandársela más tarde.",
            invitationProblem.GetProperty("detail").GetString());
        Assert.Null(await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IUserReader>().FindByEmailAsync(Email.Create(thirdEmail).Value, Ct)));

        var warnings = api.Services.GetFakeLogCollector().GetSnapshot()
            .Where(log => log.Category == typeof(EmailQuotaGuard).FullName && log.Level == LogLevel.Warning)
            .Select(log => log.Message)
            .ToList();
        Assert.Equal(2, warnings.Count);
        Assert.EndsWith(
            $"of the daily limit of {limit}; codes by email stop at {limit - reserve} and invitations at the limit",
            warnings[0],
            StringComparison.Ordinal);
        Assert.Equal(
            $"The emails with a code or an invitation of the last 24 hours reached the daily limit of {limit}; no code or invitation is sent by email until the oldest one leaves the window",
            warnings[1]);
    }

    // Documenta el costo aceptado (N7, 2026-11-16; identidad.md): en InviteOnly el de todos sube sólo con los códigos que
    // salen, y uno a un correo sin cuenta no sale. Con un único lugar libre para los códigos, el pedido que sigue a uno para
    // un correo sin cuenta pasa y el que sigue a uno para un correo con cuenta choca, así que el tope dice si el correo tenía
    // cuenta. Llegar a ese lugar cuesta llenar los de los códigos con cuentas conocidas desde 8 o más direcciones, unos 400
    // pedidos por día, mientras nadie recibe códigos por correo. No es un comportamiento a cuidar: si cambia cómo se cuenta
    // y esto deja de pasar, se corrige la documentación y se borra el test.
    [Fact]
    public async Task Documents_the_accepted_cost_with_one_place_left_the_limit_of_everyone_tells_whether_an_email_has_an_account_in_invite_only()
    {
        await using var inviteOnly = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        var known = TestEmails.Unique("oraculo-con-cuenta");
        await factory.InTransactionAsync(services =>
            services.GetRequiredService<IUserRepository>().CreateUnverifiedAsync(Email.Create(known).Value, phone: null, "Ana", "es", Ct));
        var sent = await SentByEmailInTheWindowAsync();
        // Un lugar libre para los códigos: el tope menos la reserva de las invitaciones es uno más que lo que ya salió.
        await using var api = Api(
            ("Email:DailyLimit", (sent + 2).ToString(CultureInfo.InvariantCulture)),
            ("Email:DailyInvitationReserve", "1"));
        using var client = api.CreateClient();

        using var unknownProbe = await client.PostJsonAsync(LoginCodeUrl, new { email = TestEmails.Unique("oraculo-sin-cuenta") }, "es");
        using var afterUnknown = await client.PostJsonAsync(LoginCodeUrl, new { email = TestEmails.Unique("oraculo-sin-cuenta") }, "es");
        using var knownProbe = await client.PostJsonAsync(LoginCodeUrl, new { email = known }, "es");
        using var afterKnown = await client.PostJsonAsync(LoginCodeUrl, new { email = TestEmails.Unique("oraculo-sin-cuenta") }, "es");

        Assert.Equal(HttpStatusCode.Accepted, unknownProbe.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, afterUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, knownProbe.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, afterKnown.StatusCode);
        Assert.Equal(LoginCodeErrors.DailyLimitReachedCode, (await afterKnown.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_account_at_its_daily_limit_of_profile_codes_gets_no_more_codes_by_email()
    {
        await using var api = Api(("Email:DailyProfileCodeLimitPerAccount", "1"));
        using var client = api.CreateClient();
        var account = await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>()
            .CreateUnverifiedAsync(Email.Create(TestEmails.Unique("tope-perfil")).Value, phone: null, "Ana", "es", Ct));
        var userId = account.Id.ToString("D", CultureInfo.InvariantCulture);

        using var first = await client.SendAsync(
            HttpMethod.Post, "/api/me/email/code", JsonContent.Create(new { email = TestEmails.Unique("tope-perfil-1") }), "es", userId);
        using var second = await client.SendAsync(
            HttpMethod.Post, "/api/me/email/code", JsonContent.Create(new { email = TestEmails.Unique("tope-perfil-2") }), "es", userId);
        var problem = await second.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(LoginCodeErrors.DailyLimitReachedCode, problem.GetProperty("code").GetString());
        Assert.InRange(problem.GetProperty("retryAfter").GetInt32(), 23 * 60 * 60, 24 * 60 * 60);
    }

    // En el tope del administrador, el alta con invitación por correo no queda a medias: se deshace entera.
    [Fact]
    public async Task An_administrator_at_the_daily_limit_of_invitations_by_email_neither_invites_nor_creates_with_an_invitation()
    {
        var already = await AdminInvitationsByEmailInTheWindowAsync();
        await using var api = Api(("Email:DailyInvitationLimitPerAdministrator", (already + 1).ToString(CultureInfo.InvariantCulture)));
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var firstEmail = TestEmails.Unique("tope-invitacion-1");
        var secondEmail = TestEmails.Unique("tope-invitacion-2");

        var firstId = await admin.CreateOkAsync(new { email = firstEmail, invitation = new { channel = "Email" } });
        using var created = await admin.CreateAsync(new { email = secondEmail, invitation = new { channel = "Email" } }, "es");
        // Sin invitación el alta sale igual; reenviarle una a esa cuenta, que no espera el minuto de otra, choca con el tope.
        var withoutInvitationId = await admin.CreateOkAsync(new { email = TestEmails.Unique("tope-invitacion-3") });
        using var resent = await admin.InviteAsync(withoutInvitationId, new { channel = "Email" }, "es");
        var createdProblem = await created.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, created.StatusCode);
        Assert.Equal(UserInvitationErrors.DailyLimitReachedCode, createdProblem.GetProperty("code").GetString());
        Assert.InRange(createdProblem.GetProperty("retryAfter").GetInt32(), 1, 24 * 60 * 60);
        Assert.Equal(HttpStatusCode.TooManyRequests, resent.StatusCode);
        Assert.Equal(UserInvitationErrors.DailyLimitReachedCode, (await resent.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Null(await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IUserReader>().FindByEmailAsync(Email.Create(secondEmail).Value, Ct)));
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.UserInvitations.CountAsync(row => row.UserId == firstId, Ct)));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.UserInvitations.AnyAsync(row => row.UserId == withoutInvitationId, Ct)));
    }

    /// <summary>La Api con sus topes, el log a mano y la IP del cliente que diga cada pedido.</summary>
    private WebApplicationFactory<Program> Api(params (string Key, string Value)[] settings) =>
        factory.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddLogging(logging => logging.AddFakeLogging());
                services.AddSingleton<IStartupFilter, ClientIpStartupFilter>();
            });
        });

    private static async Task<HttpResponseMessage> RequestCodeAsync(HttpClient client, string email, string clientIp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(LoginCodeUrl, UriKind.Relative))
        {
            Content = JsonContent.Create(new { email }),
        };
        request.Headers.AcceptLanguage.ParseAdd("es");
        request.Headers.Add(ClientIpHeader, clientIp);

        return await client.SendAsync(request, Ct);
    }

    // Una red /64 de documentación que no usó otro test: la base es la de toda la corrida.
    private static string UniqueNetwork() =>
        string.Create(CultureInfo.InvariantCulture, $"2001:db8:{Random.Shared.Next(1, 0x10000):x}:{Random.Shared.Next(1, 0x10000):x}");

    private Task<int> SentByEmailInTheWindowAsync() =>
        factory.ExecuteDbContextAsync(async db =>
        {
            var sinceUtc = factory.Clock.GetUtcNow().UtcDateTime.AddDays(-1);

            return await db.LoginCodes.CountAsync(code => code.Channel == LoginCodeChannel.Email && code.SentAtUtc > sinceUtc, Ct)
                + await db.UserInvitations.CountAsync(
                    invitation => invitation.Channel == UserInvitationChannel.Email && invitation.SentAtUtc > sinceUtc, Ct);
        });

    // El administrador del arnés es el mismo en toda la corrida: sus invitaciones de otros tests también cuentan.
    private Task<int> AdminInvitationsByEmailInTheWindowAsync() =>
        factory.ExecuteScopeAsync(async services =>
        {
            var admin = await services.GetRequiredService<IUserReader>().FindByEmailAsync(Email.Create(ApiFactory.AdminEmail).Value, Ct);
            if (admin is null)
            {
                return 0;
            }

            var sinceUtc = factory.Clock.GetUtcNow().UtcDateTime.AddDays(-1);
            return await services.GetRequiredService<ApplicationDbContext>().UserInvitations.CountAsync(
                invitation => invitation.Channel == UserInvitationChannel.Email && invitation.SentBy == admin.Id && invitation.SentAtUtc > sinceUtc,
                Ct);
        });

    /// <summary>Pone como IP del cliente la del header de prueba, antes de todo el pipeline: el servidor de prueba no tiene una.</summary>
    private sealed class ClientIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                if (context.Request.Headers.TryGetValue(ClientIpHeader, out var clientIp))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(clientIp.ToString());
                }

                await following();
            });
            next(app);
        };
    }
}
