namespace ArquitecturaBase.Application.Models.Settings;

/// <summary>RC6-B: the name and the sender saved together. The logo is an uploaded file (ME2).</summary>
public sealed record SystemBrandingRequest(string AppName, string EmailSenderName);
