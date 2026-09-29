using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// El texto de cada clave de pg_advisory_xact_lock del módulo WhatsApp, como AdvisoryLockKeysTests con las del núcleo. El
/// texto ES el lock: cambiarlo deja de poner en fila a quien use el texto viejo (la versión anterior de la Api durante un
/// despliegue).
/// </summary>
public sealed class WhatsAppLockKeysTests
{
    [Fact]
    public void WhatsApp_keys_keep_the_text_that_meta_sends()
    {
        Assert.Equal("whatsapp-contact:user:AR.1102953142229032", WhatsAppLockKeys.WhatsAppContactByUser("AR.1102953142229032"));
        Assert.Equal("whatsapp-contact:wa:5493413654813", WhatsAppLockKeys.WhatsAppContactByWaId("5493413654813"));
        Assert.Equal("whatsapp-message:wamid.1", WhatsAppLockKeys.WhatsAppMessage("wamid.1"));
    }
}
