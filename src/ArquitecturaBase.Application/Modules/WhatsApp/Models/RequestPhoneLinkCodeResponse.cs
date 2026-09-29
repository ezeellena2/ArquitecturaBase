namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>El número interpretado y su forma enmascarada para confirmar el vínculo.</summary>
public sealed record RequestPhoneLinkCodeResponse(int ResendAfterSeconds, string Phone, string MaskedPhone);
