using ArquitecturaBase.Application.Models.WhatsApp;

namespace ArquitecturaBase.Application.Interfaces.Integrations;

/// <summary>
/// Lee el cuerpo de un webhook de Meta, ya con la firma validada, y devuelve lo que importa. Ignora lo que no sea un
/// aviso de mensajes (<c>messages</c>) de la cuenta de WhatsApp del número configurado, y lo que no se puede leer: un
/// webhook que no sirve se descarta, porque Meta lo reintentaría igual durante días.
/// </summary>
public interface IWhatsAppWebhookReader
{
    WhatsAppWebhookBatch Read(ReadOnlyMemory<byte> body);
}
