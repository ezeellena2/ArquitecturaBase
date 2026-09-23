namespace ArquitecturaBase.Application.Abstractions.Identity;

/// <summary>
/// Los proveedores externos con el nombre que Identity guarda en cada ingreso vinculado. Es el mismo que el esquema
/// de ASP.NET Core (<c>GoogleDefaults.AuthenticationScheme</c>), que Application no puede referenciar: lo verifica
/// <c>AccountsWithPhoneTests</c> con el ingreso real.
/// </summary>
public static class ExternalLoginProviders
{
    public const string Google = "Google";
}
