using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

internal sealed record WhatsAppFlowCompilationResponse(IReadOnlyList<WhatsAppCompiledFlowResponse> Flows, IReadOnlyList<StructuredFormIssueResponse> Issues);
internal sealed record WhatsAppCompiledFlowResponse(string Culture, string Json, string Hash, int ComponentCount, IReadOnlyList<WhatsAppFlowFieldMapping> Mapping)
{
    public override string ToString() => "WhatsAppCompiledFlowResponse { Content = [REDACTED] }";
}
internal sealed record WhatsAppFlowFieldMapping(Guid FieldId, string Key, string Type, bool Required, int? MaxLength, IReadOnlyList<Guid> Choices);
internal sealed record WhatsAppFlowValueResponse(bool IsPresent, string? Text = null, string? Decimal = null, DateOnly? Date = null, bool? Boolean = null, Guid? ChoiceId = null)
{
    public override string ToString() => "WhatsAppFlowValueResponse { Content = [REDACTED] }";
}
