using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public sealed record WhatsAppFlowAssistantContent(int Format, StructuredRecordCaptureOrigin Origin,
    IReadOnlyList<WhatsAppAssistantPrefillResponse> ProposedFields)
{
    /// <summary>
    /// Task 9 of the documents: what the server adds on capture and the marks the form went with («data the server adds on capture»);
    /// omitted when absent, so the contents sealed before keep their bytes.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public WhatsAppFlowServerData? Server { get; init; }
    public override string ToString() => "WhatsAppFlowAssistantContent { Content = [REDACTED] }";
}
