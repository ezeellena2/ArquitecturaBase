using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// El alta de un administrador con correo, número o los dos (sección 12 del spec del ingreso con WhatsApp). Lo que
/// carga el admin queda sin verificar hasta que la persona entra con eso, y el número pasa por las mismas reglas que en
/// el ingreso: un celular válido de un país habilitado.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class CreateUserWithPhoneTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_user_can_be_created_with_only_a_phone_that_stays_unverified()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
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
        var admin = await AdminUsersApi.SignInAsync(factory, client);
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
        var admin = await AdminUsersApi.SignInAsync(factory, client);

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
        var admin = await AdminUsersApi.SignInAsync(factory, client);

        using var response = await admin.CreateAsync(new { phone = new { country = "AR", number = "123" } }, "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(UserErrors.PhoneInvalidCode, problem.GetProperty("code").GetString());
        Assert.Equal("Ingresá un número de celular válido.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_phone_from_a_country_that_is_not_enabled_is_rejected()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);

        using var response = await admin.CreateAsync(new { phone = new { country = "UY", number = "099 123 456" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(WhatsAppErrors.CountryNotSupportedCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_phone_with_an_active_account_is_rejected()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
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
    /// El bot («Crear cuenta») crea la cuenta del número sin el lock del destino: si la confirma entre la búsqueda del
    /// alta y su guardado, el alta choca con el índice único. La lectura vieja lo simula sin depender de cómo se crucen
    /// los dos pedidos. Es el mismo 409 que si la búsqueda la hubiera visto, y la invitación no sale.
    /// </summary>
    [Fact]
    public async Task A_phone_that_another_account_takes_between_the_check_and_the_save_answers_409()
    {
        var phone = TestPhones.Unique();
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            StaleIdentityReads.Replace(services, phone)));
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var owner = await admin.CreateVerifiedAccountAsync(email: null, phone);
        var email = TestEmails.Unique("carrera");

        using var response = await admin.CreateAsync(
            new
            {
                email,
                phone = AdminUsersApi.PhoneField(phone),
                displayName = "Laura Ríos",
                invitation = new { channel = "WhatsApp", consent = true },
            },
            "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.PhoneAlreadyExistsCode, problem.GetProperty("code").GetString());
        Assert.Equal("Ya existe una cuenta con ese número.", problem.GetProperty("detail").GetString());
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(user => user.Email == email, Ct)));
        Assert.Equal(owner.Id, await factory.ExecuteDbContextAsync(db => db.Users
            .Where(user => user.PhoneNumber == phone.Value)
            .Select(user => user.Id)
            .SingleAsync(Ct)));
        Assert.Equal(0, factory.WhatsApp.CountFor(phone));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.UserInvitations.AnyAsync(invitation => invitation.UserId == owner.Id, Ct)));
    }

    /// <summary>
    /// Lo mismo con el correo: el primer ingreso con Google crea la cuenta sin el lock del destino.
    /// </summary>
    [Fact]
    public async Task An_email_that_another_account_takes_between_the_check_and_the_save_answers_409()
    {
        var email = TestEmails.Unique("carreracorreo");
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            StaleIdentityReads.Replace(services, Email.Create(email).Value)));
        using var client = api.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var owner = await admin.CreateVerifiedAccountAsync(email, phone: null);

        using var response = await admin.CreateAsync(new { email, displayName = "Laura Ríos" }, "es");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();
        var deleted = await admin.CreateVerifiedAccountAsync(email: null, phone, "Antes");
        await admin.DeleteAccountAsync(deleted.Id);

        var restored = await admin.CreateOkAsync(new
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
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var email = TestEmails.Unique("ambos");
        var phone = TestPhones.Unique();
        var deleted = await admin.CreateVerifiedAccountAsync(email, phone);
        await admin.DeleteAccountAsync(deleted.Id);

        var restored = await admin.CreateOkAsync(new { email, phone = AdminUsersApi.PhoneField(phone) });

        Assert.Equal(deleted.Id, restored);
        var account = await admin.AccountAsync(restored);
        Assert.Equal(email, account.Email);
        Assert.Equal(phone.Value, account.PhoneNumber);
    }

    [Fact]
    public async Task The_phone_of_a_deleted_account_with_another_email_is_rejected()
    {
        // El correo es nuevo y el número es de una cuenta borrada que tiene otro correo: el alta no puede ser las dos.
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();
        var deleted = await admin.CreateVerifiedAccountAsync(TestEmails.Unique("borrada"), phone);
        await admin.DeleteAccountAsync(deleted.Id);
        var email = TestEmails.Unique("nueva");

        using var response = await admin.CreateAsync(new { email, phone = AdminUsersApi.PhoneField(phone) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.PhoneAlreadyExistsCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(user => user.Email == email, Ct)));
    }

    [Fact]
    public async Task An_email_and_a_phone_of_two_different_deleted_accounts_are_rejected()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
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
    public async Task The_email_of_a_deleted_account_with_another_phone_is_rejected()
    {
        // Restaurarla le cambiaría el número a una cuenta con historia: el alta describe a otra persona, o a la misma con
        // otro número, y eso lo tiene que resolver el admin.
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var email = TestEmails.Unique("otronumero");
        var deleted = await admin.CreateVerifiedAccountAsync(email, TestPhones.Unique());
        await admin.DeleteAccountAsync(deleted.Id);

        using var response = await admin.CreateAsync(new { email, phone = AdminUsersApi.PhoneField(TestPhones.Unique()) }, "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.AlreadyExistsCode, problem.GetProperty("code").GetString());
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(user => user.Id == deleted.Id, Ct)));
    }

    [Fact]
    public async Task A_deleted_account_without_a_phone_is_restored_with_the_phone_that_the_administrator_loads()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var email = TestEmails.Unique("sinnumero");
        var phone = TestPhones.Unique();
        var deleted = await admin.CreateVerifiedAccountAsync(email, phone: null);
        await admin.DeleteAccountAsync(deleted.Id);

        var restored = await admin.CreateOkAsync(new { email, phone = AdminUsersApi.PhoneField(phone) });

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
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();
        var userId = await admin.CreateOkAsync(new { phone = AdminUsersApi.PhoneField(phone) });

        using var response = await client.GetWithTokenAsync("/api/users?search=" + TestPhones.LocalPart(phone), admin.AccessToken);
        var row = (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == userId);

        var local = TestPhones.LocalPart(phone);
        Assert.Equal($"+54 9 351 {local[..3]}-{local[3..]}", row.GetProperty("formattedPhoneNumber").GetString());
        Assert.False(row.GetProperty("phoneNumberConfirmed").GetBoolean());
    }

    private static string[] Strings(JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!)];
}
