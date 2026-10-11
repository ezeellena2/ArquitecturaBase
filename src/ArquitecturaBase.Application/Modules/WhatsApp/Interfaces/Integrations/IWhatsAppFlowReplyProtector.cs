namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

public interface IWhatsAppFlowReplyProtector
{
    byte[] Protect(string waMessageId, Guid contactId, string responseJson);
    string Read(string waMessageId, Guid contactId, byte[] protectedResponse);
}
