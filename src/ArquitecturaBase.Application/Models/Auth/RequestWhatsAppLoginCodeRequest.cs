namespace ArquitecturaBase.Application.Models.Auth;

/// <summary>El número como lo escribió la persona y el país elegido para interpretar números sin prefijo.</summary>
public sealed record RequestWhatsAppLoginCodeRequest(string? Country, string? Number);
