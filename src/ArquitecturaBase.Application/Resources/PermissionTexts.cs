using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Resources;

/// <summary>
/// Nombres y descripciones del catálogo de permisos en el idioma de la petición. La clave del área es el prefijo del
/// código (<c>Area.users</c>); las del permiso llevan el código entero (<c>Permission.users.read</c> y
/// <c>PermissionDescription.users.read</c>).
/// </summary>
public static class PermissionTexts
{
    internal static ResourceManager ResourceManager { get; } =
        new("ArquitecturaBase.Application.Resources.Permissions", typeof(PermissionTexts).Assembly);

    public static string Area(string area) => Get("Area." + area);

    public static string Permission(string permission) => Get("Permission." + permission);

    public static string Description(string permission) => Get("PermissionDescription." + permission);

    private static string Get(string key) => ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
