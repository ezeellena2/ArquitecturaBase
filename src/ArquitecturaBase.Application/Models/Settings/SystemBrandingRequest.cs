namespace ArquitecturaBase.Application.Models.Settings;

/// <summary>RC6-B: the name, the sender and the logo saved together; an empty or missing logo means none.</summary>
public sealed record SystemBrandingRequest(string AppName, string EmailSenderName, string? EmailLogoUrl);
