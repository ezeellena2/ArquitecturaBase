namespace ArquitecturaBase.Application.Models.Roles;

/// <summary>Un permiso del catálogo: código estable, nombre y descripción traducidos.</summary>
public sealed record PermissionItem(string Code, string Name, string Description);

/// <summary>Permisos de un área, identificada por el prefijo del código.</summary>
public sealed record PermissionGroup(string Area, string Name, IReadOnlyCollection<PermissionItem> Permissions);
