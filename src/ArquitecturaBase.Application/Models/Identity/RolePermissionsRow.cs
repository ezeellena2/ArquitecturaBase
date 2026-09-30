namespace ArquitecturaBase.Application.Models.Identity;

/// <summary>Permissions belonging to a confirmed role version.</summary>
public sealed record RolePermissionsRow(string Version, string[] Permissions);
