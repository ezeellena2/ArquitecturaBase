namespace ArquitecturaBase.Application.Models.Settings;

/// <summary>ME7: what the sign-in and the shell show before the session: the name and the versions of the logo and the icon.</summary>
public sealed record SystemPresentationBrandResponse(string AppName, string? LogoVersion, string? IconVersion);
