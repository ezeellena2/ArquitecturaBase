using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public sealed record WhatsAppFlowAssistantContent(int Format, StructuredRecordCaptureOrigin Origin,
    IReadOnlyList<WhatsAppAssistantPrefillResponse> ProposedFields)
{
    public override string ToString() => "WhatsAppFlowAssistantContent { Content = [REDACTED] }";
}
