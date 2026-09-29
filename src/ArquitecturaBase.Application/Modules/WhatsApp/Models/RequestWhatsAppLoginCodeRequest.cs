namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>El número como lo escribió la persona y el país elegido para interpretar números sin prefijo.</summary>
public sealed record RequestWhatsAppLoginCodeRequest(string? Country, string? Number);
