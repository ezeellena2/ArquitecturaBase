using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// El texto de cada clave de pg_advisory_xact_lock. El texto ES el lock: cambiarlo deja de poner en fila a quien use el
/// texto viejo (la versión anterior de la Api durante un despliegue) o separa casos de uso que hoy se esperan entre sí.
/// </summary>
public sealed class AdvisoryLockKeysTests
{
    private static readonly Guid AccountId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

    [Fact]
    public void The_login_code_key_is_the_normalized_destination()
    {
        Assert.Equal(
            "login-code:ana@example.com",
            AdvisoryLockKeys.LoginCode(LoginCodeDestination.ForEmail(Email.Create(" Ana@Example.com ").Value)));
        Assert.Equal(
            "login-code:+5493515550101",
            AdvisoryLockKeys.LoginCode(LoginCodeDestination.ForPhone(PhoneNumber.Create("+5493515550101").Value)));
    }

    [Fact]
    public void Account_keys_use_the_id_in_n_format()
    {
        Assert.Equal("login-link:0123456789abcdef0123456789abcdef", AdvisoryLockKeys.LoginLink(AccountId));
        Assert.Equal("user-invitation:0123456789abcdef0123456789abcdef", AdvisoryLockKeys.UserInvitation(AccountId));
    }

    [Fact]
    public void WhatsApp_keys_keep_the_text_that_meta_sends()
    {
        Assert.Equal("whatsapp-contact:user:AR.1102953142229032", AdvisoryLockKeys.WhatsAppContactByUser("AR.1102953142229032"));
        Assert.Equal("whatsapp-contact:wa:5493413654813", AdvisoryLockKeys.WhatsAppContactByWaId("5493413654813"));
        Assert.Equal("whatsapp-message:wamid.1", AdvisoryLockKeys.WhatsAppMessage("wamid.1"));
    }

    [Fact]
    public void The_external_login_key_is_the_provider_and_its_key()
    {
        Assert.Equal("external-login:Google:k", AdvisoryLockKeys.ExternalLogin("Google", "k"));
    }

    [Fact]
    public void The_seed_key_is_fixed()
    {
        // Lo comparten todas las réplicas y todas las versiones de la Api: una que arranca durante un despliegue tiene que
        // esperar a la que ya está sembrando.
        Assert.Equal("seed:database", AdvisoryLockKeys.Seed);
    }
}
