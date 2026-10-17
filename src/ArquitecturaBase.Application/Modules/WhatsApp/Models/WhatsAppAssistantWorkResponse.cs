using ArquitecturaBase.Application.Models.Assistant;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;
public sealed record WhatsAppAssistantWorkResponse(Guid RequestId,AssistantOperationRequest Operation)
{
    public override string ToString()=>"WhatsAppAssistantWorkResponse { Content = [REDACTED] }";
}
