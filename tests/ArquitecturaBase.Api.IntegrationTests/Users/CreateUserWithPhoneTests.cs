using System.Globalization;
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Emails;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// El alta de un administrador con correo, número o los dos (sección 12 del spec del ingreso con WhatsApp). Lo que
/// carga el admin queda sin verificar hasta que la persona entra con eso, y el número pasa por las mismas reglas que en
/// el ingreso: un celular válido, y con un canal de teléfono (WhatsApp), de uno de sus países. El administrador pide con
/// la sesión de prueba: acá no se prueba su ingreso.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class CreateUserWithPhoneTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_user_can_be_created_with_only_a_phone_that_stays_unverified()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var phone = TestPhones.Unique();

        var userId = await admin.CreateOkAsync(new
        {
            phone = AdminUsersApi.PhoneField(phone),
            displayName = "Laura Ríos",
            roles = new[] { SystemRoles.User },
        });

        var account = await admin.AccountAsync(userId);
        Assert.Null(account.Email);
        Assert.Equal(phone.Value, account.PhoneNumber);
        Assert.False(account.PhoneNumberConfirmed);
        Assert.Equal("Laura Ríos", account.DisplayName);

        // El detalle lo muestra formateado, como /api/me: el front nunca muestra el E.164.
        var detail = await admin.DetailAsync(userId);
        var local = TestPhones.LocalPart(phone);
        Assert.Equal($"+54 9 351 {local[..3]}-{local[3..]}", detail.GetProperty("formattedPhoneNumber").GetString());
        Assert.False(detail.GetProperty("phoneNumberConfirmed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("lastInvitation").ValueKind);
    }

    [Fact]
    public async Task The_email_that_an_administrator_loads_stays_unverified_until_the_person_signs_in_with_it()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var email = TestEmails.Unique("sinverificar");
        var userId = await admin.CreateOkAsync(new { email });

        Assert.False((await admin.AccountAsync(userId)).EmailConfirmed);

        using var person = factory.CreateClient();
        await person.SignInWithCodeAsync(factory, email);

        Assert.True((await admin.AccountAsync(userId)).EmailConfirmed);
    }

    [Fact]
    public async Task Without_an_email_or_a_phone_the_request_is_rejected()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);

        using var response = await admin.CreateAsync(new { displayName = "Nadie", phone = new { country = "AR", number = "" } }, "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(UserErrors.IdentityRequiredCode, problem.GetProperty("code").GetString());
        Assert.Equal("Cargá un correo o un número de WhatsApp.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_phone_that_is_not_a_mobile_is_rejected()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);

        using var response = await admin.CreateAsync(new { phone = new { country = "AR", number = "123" } }, "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(UserErrors.PhoneInvalidCode, problem.GetProperty("code").GetString());
        Assert.Equal("Ingresá un número de celular válido.", problem.GetProperty("detail").GetString());
    }

    /// <summary>
    /// Sin un canal de teléfono, nada limita el país: basta un celular válido (decisión 4 de la Etapa 6). Con WhatsApp, su
    /// canal pone la regla de los países; el test lo cambia por el apagado para probar lo mismo con y sin el módulo.
    /// </summary>
    [Fact]
    public async Task Without_a_phone_channel_a_phone_from_any_country_is_accepted()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IPhoneChannel, DisabledPhoneChannel>())));
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var local = "99" + Random.Shared.Next(100_000, 1_000_000).ToString(CultureInfo.InvariantCulture);

        var userId = await admin.CreateOkAsync(new
        {
            phone = new { country = "UY", number = "0" + local },
            displayName = "Laura Ríos",
            roles = new[] { SystemRoles.User },
        });

        var account = await admin.AccountAsync(userId);
        Assert.Equal("+598" + local, account.PhoneNumber);
        Assert.False(account.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task A_phone_with_an_active_account_is_rejected()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var phone = TestPhones.Unique();
        await admin.CreateVerifiedAccountAsync(email: null, phone);

        using var response = await admin.CreateAsync(
            new { email = TestEmails.Unique("otronumero"), phone = AdminUsersApi.PhoneField(phone) }, "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.PhoneAlreadyExistsCode, problem.GetProperty("code").GetString());
        Assert.Equal("Ya existe una cuenta con ese número.", problem.GetProperty("detail").GetString());
    }

    /// <summary>
    /// Otra cuenta con el mismo número se crea sin el lock del destino (con WhatsApp, el bot con «Crear cuenta»): si la
    /// confirma entre la búsqueda del alta y su guardado, el alta choca con el índice único. La lectura vieja lo simula sin
    /// depender de cómo se crucen los dos pedidos. Es el mismo 409 que si la búsqueda la hubiera visto, y la invitación por
    /// correo no sale.
    /// </summary>
    [Fact]
    public async Task A_phone_that_another_account_takes_between_the_check_and_the_save_answers_409()
    {
        var phone = TestPhones.Unique();
        var probe = new StaleReadsProbe();
        var emailQueue = new SwitchableEmailQueue();
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            StaleUserReads.Replace(services, phone, probe);
            services.RemoveAll<IEmailQueue>();
            services.AddScoped<IEmailQueue>(provider => emailQueue.Over(provider.GetRequiredService<EmailQueue>()));
        }));
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var owner = await admin.CreateVerifiedAccountAsync(email: null, phone);
        var email = TestEmails.Unique("carrera");

        using var response = await admin.CreateAsync(
            new
            {
                email,
                phone = AdminUsersApi.PhoneField(phone),
                displayName = "Laura Ríos",
                invitation = new { channel = "Email" },
            },
            "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        // El chequeo previo leyó por la lectura vieja: el 409 salió del choque con el índice único, no de él.
        Assert.True(probe.HiddenLookups > 0);
        Assert.Equal(UserErrors.PhoneAlreadyExistsCode, problem.GetProperty("code").GetString());
        Assert.Equal("Ya existe una cuenta con ese número.", problem.GetProperty("detail").GetString());
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(user => user.Email == email, Ct)));
        Assert.Equal(owner.Id, await factory.ExecuteDbContextAsync(db => db.Users
            .Where(user => user.PhoneNumber == phone.Value)
            .Select(user => user.Id)
            .SingleAsync(Ct)));

        // El código del administrador también pasa por la cola: se mira solo el destino del alta.
        Assert.DoesNotContain(emailQueue.Queued, message => message.To == email);
    }

    /// <summary>
    /// Lo mismo con el correo: el primer ingreso con Google crea la cuenta sin el lock del destino.
    /// </summary>
    [Fact]
    public async Task An_email_that_another_account_takes_between_the_check_and_the_save_answers_409()
    {
        var email = TestEmails.Unique("carreracorreo");
        var probe = new StaleReadsProbe();
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            StaleUserReads.Replace(services, Email.Create(email).Value, probe)));
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var owner = await admin.CreateVerifiedAccountAsync(email, phone: null);

        using var response = await admin.CreateAsync(new { email, displayName = "Laura Ríos" }, "es");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        // El chequeo previo leyó por la lectura vieja: el 409 salió del choque con el índice único, no de él.
        Assert.True(probe.HiddenLookups > 0);
        Assert.Equal(UserErrors.AlreadyExistsCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(owner.Id, await factory.ExecuteDbContextAsync(db => db.Users
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync(Ct)));
    }

    [Fact]
    public async Task The_phone_of_a_deleted_account_restores_it_with_the_new_roles_and_the_phone_unverified()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var phone = TestPhones.Unique();
        var deleted = await admin.CreateVerifiedAccountAsync(email: null, phone, "Antes");
        await admin.DeleteAccountAsync(deleted.Id);

        var restored = await admin.RestoreOkAsync(new
        {
            phone = AdminUsersApi.PhoneField(phone),
            displayName = "De vuelta",
            roles = new[] { SystemRoles.Admin },
        });

        Assert.Equal(deleted.Id, restored);
        var account = await admin.AccountAsync(restored);
        Assert.True(account.IsActive);
        Assert.Equal("De vuelta", account.DisplayName);
        Assert.False(account.PhoneNumberConfirmed);
        Assert.Equal([SystemRoles.Admin], Strings(await admin.DetailAsync(restored), "roles"));
    }

    [Fact]
    public async Task The_email_and_the_phone_of_the_same_deleted_account_restore_it()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var email = TestEmails.Unique("ambos");
        var phone = TestPhones.Unique();
        var deleted = await admin.CreateVerifiedAccountAsync(email, phone);
        await admin.DeleteAccountAsync(deleted.Id);

        var restored = await admin.RestoreOkAsync(new { email, phone = AdminUsersApi.PhoneField(phone) });

        Assert.Equal(deleted.Id, restored);
        var account = await admin.AccountAsync(restored);
        Assert.Equal(email, account.Email);
        Assert.Equal(phone.Value, account.PhoneNumber);
    }

    /// <summary>
    /// N4: el celular de la empresa pasa de Diego a Nora. Restaurar por el número le devuelve a Nora la cuenta de Diego,
    /// con su historia, pero sin el correo ni el Google de Diego, que ya no entra a ella por ningún lado: su sesión de
    /// antes del borrado no vale, ni siquiera un refresh token que se hubiera salvado del corte del borrado, y un código a
    /// su correo o Google lo llevan a otra cuenta.
    /// </summary>
    [Fact]
    public async Task Restoring_by_the_phone_leaves_the_previous_owner_out_of_the_account()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var email = TestEmails.Unique("diego");
        var phone = TestPhones.Unique();
        var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var diego = await admin.CreateVerifiedAccountAsync(email, phone, "Diego");
        await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().AddExternalLoginAsync(
            diego.Id, new ExternalLogin(ExternalLoginProviders.Google, providerKey, email, true, "Diego"), Ct));
        using var diegoBefore = factory.CreateClient();
        var diegoTokens = await diegoBefore.LoginAsync(factory, email);
        using (var deleted = await admin.SendAsync(HttpMethod.Delete, $"/api/users/{diego.Id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        await ReviveTokensAsync(diego.Id);
        var nora = await admin.RestoreOkAsync(new { phone = AdminUsersApi.PhoneField(phone), displayName = "Nora" });

        Assert.Equal(diego.Id, nora);
        var account = await admin.AccountAsync(nora);
        Assert.Equal("Nora", account.DisplayName);
        Assert.Null(account.Email);
        Assert.False(account.EmailConfirmed);
        Assert.Equal(phone.Value, account.PhoneNumber);
        Assert.False(account.PhoneNumberConfirmed);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.UserLogins.AnyAsync(login => login.UserId == nora, Ct)));

        using (var me = await diegoBefore.GetWithTokenAsync("/api/me", diegoTokens.AccessToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        }

        using (var refresh = await diegoBefore.RefreshAsync(diegoTokens.RefreshToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
            Assert.Equal("invalid_grant", (await refresh.ReadJsonAsync()).GetProperty("error").GetString());
        }

        using var byCode = factory.CreateClient();
        var codeTokens = await byCode.LoginAsync(factory, email);
        using (var me = await byCode.GetWithTokenAsync("/api/me", codeTokens.AccessToken))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.NotEqual(nora, (await me.ReadJsonAsync()).GetProperty("id").GetGuid());
        }

        using var byGoogle = factory.CreateClient();
        using (var external = await byGoogle.PostJsonAsync(
            "/test/external-login", new { providerKey, email, name = "Diego", emailVerified = true }))
        {
            Assert.True(external.IsSuccessStatusCode);
        }

        using var callback = await byGoogle.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(GoogleReturnUrl));
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(GoogleReturnUrl, callback.Headers.Location!.OriginalString);
        var googleAccount = await factory.ExecuteDbContextAsync(db => db.UserLogins
            .Where(login => login.LoginProvider == ExternalLoginProviders.Google && login.ProviderKey == providerKey)
            .Select(login => login.UserId)
            .SingleAsync(Ct));
        Assert.NotEqual(nora, googleAccount);
        Assert.Null((await admin.AccountAsync(nora)).Email);
    }

    [Fact]
    public async Task Restoring_by_the_email_removes_the_previous_phone()
    {
        // Sin número en el alta, la cuenta vuelve sin el de antes, que queda libre para otra alta.
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var email = TestEmails.Unique("sincelular");
        var phone = TestPhones.Unique();
        var deleted = await admin.CreateVerifiedAccountAsync(email, phone);
        await admin.DeleteAccountAsync(deleted.Id);

        var restored = await admin.RestoreOkAsync(new { email });

        Assert.Equal(deleted.Id, restored);
        var account = await admin.AccountAsync(restored);
        Assert.Equal(email, account.Email);
        Assert.False(account.EmailConfirmed);
        Assert.Null(account.PhoneNumber);
        var other = await admin.CreatedAsync(new { phone = AdminUsersApi.PhoneField(phone) });
        Assert.NotEqual(restored, other.GetProperty("id").GetGuid());
        Assert.False(other.GetProperty("restored").GetBoolean());
    }

    [Fact]
    public async Task The_phone_of_a_deleted_account_with_another_email_restores_it_with_the_new_email()
    {
        // N4: Nora llega con su correo y el celular de la empresa, que era de la cuenta borrada de Diego, con otro correo.
        // La cuenta vuelve sólo con lo que trajo el alta: el correo de Diego se reemplaza por el de Nora, sin verificar.
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var phone = TestPhones.Unique();
        var previousEmail = TestEmails.Unique("borrada");
        var deleted = await admin.CreateVerifiedAccountAsync(previousEmail, phone);
        await admin.DeleteAccountAsync(deleted.Id);
        var email = TestEmails.Unique("nueva");

        var restored = await admin.RestoreOkAsync(new { email, phone = AdminUsersApi.PhoneField(phone) });

        Assert.Equal(deleted.Id, restored);
        var account = await admin.AccountAsync(restored);
        Assert.Equal(email, account.Email);
        Assert.False(account.EmailConfirmed);
        Assert.Equal(phone.Value, account.PhoneNumber);
        Assert.False(account.PhoneNumberConfirmed);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users
            .IgnoreQueryFilters()
            .AnyAsync(user => user.Email == previousEmail, Ct)));
    }

    [Fact]
    public async Task An_email_and_a_phone_of_two_different_deleted_accounts_are_rejected()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var email = TestEmails.Unique("primera");
        var phone = TestPhones.Unique();
        var byEmail = await admin.CreateVerifiedAccountAsync(email, phone: null);
        var byPhone = await admin.CreateVerifiedAccountAsync(email: null, phone);
        await admin.DeleteAccountAsync(byEmail.Id);
        await admin.DeleteAccountAsync(byPhone.Id);

        using var response = await admin.CreateAsync(new { email, phone = AdminUsersApi.PhoneField(phone) });

        // El correo manda qué cuenta se restaura; el que choca es el número, que es de otra.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.PhoneAlreadyExistsCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(user => user.Id == byEmail.Id, Ct)));
    }

    [Fact]
    public async Task The_email_of_a_deleted_account_with_another_phone_restores_it_with_the_new_phone()
    {
        // N4: el número de la cuenta borrada se reemplaza por el del alta, sin verificar, y el de antes queda libre.
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var email = TestEmails.Unique("otronumero");
        var previousPhone = TestPhones.Unique();
        var deleted = await admin.CreateVerifiedAccountAsync(email, previousPhone);
        await admin.DeleteAccountAsync(deleted.Id);
        var phone = TestPhones.Unique();

        var restored = await admin.RestoreOkAsync(new { email, phone = AdminUsersApi.PhoneField(phone) });

        Assert.Equal(deleted.Id, restored);
        var account = await admin.AccountAsync(restored);
        Assert.Equal(email, account.Email);
        Assert.Equal(phone.Value, account.PhoneNumber);
        Assert.False(account.PhoneNumberConfirmed);
        var other = await admin.CreatedAsync(new { phone = AdminUsersApi.PhoneField(previousPhone) });
        Assert.NotEqual(restored, other.GetProperty("id").GetGuid());
        Assert.False(other.GetProperty("restored").GetBoolean());
    }

    [Fact]
    public async Task The_email_of_a_deleted_account_with_the_phone_of_an_active_one_is_rejected()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var email = TestEmails.Unique("borradaconotro");
        var deleted = await admin.CreateVerifiedAccountAsync(email, TestPhones.Unique());
        await admin.DeleteAccountAsync(deleted.Id);
        var phone = TestPhones.Unique();
        await admin.CreateVerifiedAccountAsync(email: null, phone);

        using var response = await admin.CreateAsync(new { email, phone = AdminUsersApi.PhoneField(phone) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.PhoneAlreadyExistsCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(user => user.Id == deleted.Id, Ct)));
    }

    [Fact]
    public async Task A_deleted_account_without_a_phone_is_restored_with_the_phone_that_the_administrator_loads()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var email = TestEmails.Unique("sinnumero");
        var phone = TestPhones.Unique();
        var deleted = await admin.CreateVerifiedAccountAsync(email, phone: null);
        await admin.DeleteAccountAsync(deleted.Id);

        var restored = await admin.RestoreOkAsync(new { email, phone = AdminUsersApi.PhoneField(phone) });

        Assert.Equal(deleted.Id, restored);
        var account = await admin.AccountAsync(restored);
        Assert.Equal(phone.Value, account.PhoneNumber);
        Assert.False(account.PhoneNumberConfirmed);
        Assert.False(account.EmailConfirmed);
    }

    [Fact]
    public async Task The_list_brings_the_phone_formatted()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.WithTestSessionAsync(factory, client);
        var phone = TestPhones.Unique();
        var userId = await admin.CreateOkAsync(new { phone = AdminUsersApi.PhoneField(phone) });

        using var response = await admin.SendAsync(HttpMethod.Get, "/api/users?search=" + TestPhones.LocalPart(phone));
        var row = (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == userId);

        var local = TestPhones.LocalPart(phone);
        Assert.Equal($"+54 9 351 {local[..3]}-{local[3..]}", row.GetProperty("formattedPhoneNumber").GetString());
        Assert.False(row.GetProperty("phoneNumberConfirmed").GetBoolean());
    }

    /// <summary>A dónde vuelve el ingreso con Google, como en <c>ExternalLoginTests</c>.</summary>
    private const string GoogleReturnUrl = "/connect/authorize?client_id=web";

    /// <summary>
    /// Deja otra vez válidos los tokens y las autorizaciones de la cuenta que el borrado revocó: lo que pasaría con uno
    /// que se salvó del corte (por ejemplo, emitido en paralelo con el borrado).
    /// </summary>
    private Task ReviveTokensAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(async db =>
        {
            var subject = userId.ToString("D", CultureInfo.InvariantCulture);
            foreach (var token in await db.Set<OpenIddictEntityFrameworkCoreToken<Guid>>()
                .Where(token => token.Subject == subject)
                .ToListAsync(Ct))
            {
                token.Status = OpenIddictConstants.Statuses.Valid;
            }

            foreach (var authorization in await db.Set<OpenIddictEntityFrameworkCoreAuthorization<Guid>>()
                .Where(authorization => authorization.Subject == subject)
                .ToListAsync(Ct))
            {
                authorization.Status = OpenIddictConstants.Statuses.Valid;
            }

            await db.SaveChangesAsync(Ct);
        });

    private static string[] Strings(JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!)];
}
