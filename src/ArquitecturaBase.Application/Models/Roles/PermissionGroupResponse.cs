namespace ArquitecturaBase.Application.Models.Roles;

/// <summary>
/// Un permiso del catálogo: código estable, nombre y descripción traducidos, y los códigos que incluye en el orden del
/// catálogo (quien lo tiene, tiene también esos; guardar un rol los guarda).
/// </summary>
public sealed record PermissionItem(string Code, string Name, string Description, IReadOnlyCollection<string> Includes);

/// <summary>Permisos de un área, identificada por el prefijo del código.</summary>
public sealed record PermissionGroupResponse(string Area, string Name, IReadOnlyCollection<PermissionItem> Permissions);
