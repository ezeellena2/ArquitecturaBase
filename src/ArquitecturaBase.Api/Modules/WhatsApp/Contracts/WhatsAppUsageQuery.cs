namespace ArquitecturaBase.Api.Modules.WhatsApp.Contracts;

/// <summary>Consumo de WhatsApp (T7): the business month, as <c>yyyy-MM</c>.</summary>
public sealed record WhatsAppUsageQuery(string? Month = null);
