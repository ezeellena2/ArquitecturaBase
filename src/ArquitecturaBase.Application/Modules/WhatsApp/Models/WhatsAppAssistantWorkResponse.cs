using ArquitecturaBase.Application.Models.Assistant;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;
public sealed record WhatsAppAssistantWorkResponse(Guid RequestId,AssistantOperationRequest Operation)
{
    public bool ShouldSubmit {get;init;}=true;
    public override string ToString()=>"WhatsAppAssistantWorkResponse { Content = [REDACTED] }";
}
