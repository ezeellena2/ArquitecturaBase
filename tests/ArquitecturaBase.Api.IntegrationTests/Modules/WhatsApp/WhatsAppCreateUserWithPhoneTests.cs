using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// La parte del módulo WhatsApp del alta con número (CreateUserWithPhoneTests): el país tiene que estar habilitado,
/// y la carrera con el bot, con la invitación por WhatsApp, responde el mismo 409 sin mandar nada.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppCreateUserWithPhoneTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_phone_from_a_country_that_is_not_enabled_is_rejected()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);

        using var response = await admin.CreateAsync(new { phone = new { country = "UY", number = "099 123 456" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(WhatsAppErrors.CountryNotSupportedCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
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
        var probe = new StaleReadsProbe();
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            StaleUserReads.Replace(services, phone, probe)));
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
        // El chequeo previo leyó por la lectura vieja: el 409 salió del choque con el índice único, no de él.
        Assert.True(probe.HiddenLookups > 0);
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
}
