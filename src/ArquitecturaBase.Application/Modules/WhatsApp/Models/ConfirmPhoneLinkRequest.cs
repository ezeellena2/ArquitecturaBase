namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>El número internacional y el código recibido por WhatsApp para vincularlo a la cuenta.</summary>
public sealed record ConfirmPhoneLinkRequest(string? Phone, string? Code);
