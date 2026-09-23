namespace ArquitecturaBase.Application.Abstractions.WhatsApp;

/// <summary>
/// Si WhatsApp está configurado. Sin <c>WhatsApp:PhoneNumberId</c> queda apagado y la app arranca igual, como Google
/// sin su ClientId: el ingreso con WhatsApp no se ofrece y sus endpoints responden 404.
/// </summary>
public interface IWhatsAppAvailability
{
    bool IsEnabled { get; }

    /// <summary>
    /// Si se reciben los webhooks de Meta: hace falta WhatsApp prendido y sus dos secretos, <c>WhatsApp:AppSecret</c> y
    /// <c>WhatsApp:VerifyToken</c>. Sin ninguno de los dos, el envío funciona igual y las rutas del webhook no existen.
    /// </summary>
    bool IsWebhookEnabled { get; }
}
