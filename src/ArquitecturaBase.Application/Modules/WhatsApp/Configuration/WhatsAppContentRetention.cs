namespace ArquitecturaBase.Application.Modules.WhatsApp.Configuration;

/// <summary>One immutable projection of the existing, validated WhatsApp retention setting.</summary>
public sealed record WhatsAppContentRetention(int MessageRetentionDays);
