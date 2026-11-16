namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// El número internacional y el código recibido por WhatsApp para vincularlo a la cuenta. Con un correo verificado,
/// lleva también el código que confirma el cambio, mandado a ese correo (cada medio confirma los cambios del otro).
/// </summary>
public sealed record ConfirmPhoneLinkRequest(string? Phone, string? Code, string? EmailCode = null);
