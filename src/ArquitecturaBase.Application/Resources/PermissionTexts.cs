using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Resources;

/// <summary>
/// Nombres del catálogo de permisos en el idioma de la petición. La clave del área es el prefijo del código
/// (<c>Area.users</c>) y la del permiso, el código entero (<c>Permission.users.read</c>).
/// </summary>
public static class PermissionTexts
{
    internal static ResourceManager ResourceManager { get; } =
        new("ArquitecturaBase.Application.Resources.Permissions", typeof(PermissionTexts).Assembly);

    public static string Area(string area) => Get("Area." + area);

    public static string Permission(string permission) => Get("Permission." + permission);

    private static string Get(string key) => ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
