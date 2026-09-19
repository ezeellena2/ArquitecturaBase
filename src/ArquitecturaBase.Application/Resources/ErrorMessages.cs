using System.Globalization;
using System.Resources;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Resources;

/// <summary>
/// Textos de Errors.resx. La clave de un error es su código (Area.Entidad.Motivo).
/// El idioma sale de <see cref="CultureInfo.CurrentUICulture"/>, que en la API fija RequestLocalization.
/// </summary>
public static class ErrorMessages
{
    internal static ResourceManager ResourceManager { get; } =
        new("ArquitecturaBase.Application.Resources.Errors", typeof(ErrorMessages).Assembly);

    public static string? Find(string key) => ResourceManager.GetString(key, CultureInfo.CurrentUICulture);

    public static string Get(string key) => Find(key) ?? key;

    public static string Title(ErrorType type) => Get("Title." + Enum.GetName(type));
}
