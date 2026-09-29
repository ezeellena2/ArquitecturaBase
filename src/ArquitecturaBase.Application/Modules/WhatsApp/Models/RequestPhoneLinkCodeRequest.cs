namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>El número que la persona quiere vincular a su cuenta, tal como lo escribió, y el país elegido.</summary>
public sealed record RequestPhoneLinkCodeRequest(string? Country, string? Number);
