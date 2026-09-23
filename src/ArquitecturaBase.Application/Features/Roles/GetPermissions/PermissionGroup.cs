namespace ArquitecturaBase.Application.Features.Roles.GetPermissions;

/// <summary>Un permiso del catálogo: el código estable, y su nombre y su descripción traducidos.</summary>
public sealed record PermissionItem(string Code, string Name, string Description);

/// <summary>Los permisos de un área, que es el prefijo del código (<c>users</c>, <c>roles</c>, <c>settings</c>).</summary>
public sealed record PermissionGroup(string Area, string Name, IReadOnlyCollection<PermissionItem> Permissions);
