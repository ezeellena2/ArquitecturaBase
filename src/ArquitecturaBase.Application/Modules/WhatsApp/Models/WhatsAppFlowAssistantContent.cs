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
    /// <summary>
    /// Task 9c of the documents: what the person sent in the reply a fixed validation rejected, which the new send opens with over what
    /// was proposed; omitted otherwise, so the contents sealed before keep their bytes.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<StructuredRecordProposedValue>? Answers { get; init; }
    public override string ToString() => "WhatsAppFlowAssistantContent { Content = [REDACTED] }";
}
