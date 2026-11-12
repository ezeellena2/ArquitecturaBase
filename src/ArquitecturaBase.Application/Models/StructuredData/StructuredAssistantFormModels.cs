namespace ArquitecturaBase.Application.Models.StructuredData;

/// <summary>
/// A published form a channel's assistant can send, named in the culture asked for, with «Cuándo lo ofrece» when the form has one
/// (Casos del asistente: the forms block and the forms a case can send).
/// </summary>
public sealed record StructuredAssistantFormRow(Guid DefinitionId, string Name, string? OfferWhen);
