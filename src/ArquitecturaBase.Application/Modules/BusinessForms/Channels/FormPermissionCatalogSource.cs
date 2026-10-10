using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;
internal sealed class FormPermissionCatalogSource : IPermissionCatalogSource
{
    internal static ResourceManager ResourceManager { get; } = new("ArquitecturaBase.Application.Modules.BusinessForms.Resources.Permissions", typeof(FormPermissionCatalogSource).Assembly);
    public IReadOnlyCollection<string> Codes => BusinessFormsPermissions.All;
    public string Area(string area) => Text("Area." + area);
    public string Name(string code) => Text("Permission." + code);
    public string Description(string code) => Text("PermissionDescription." + code);
    private static string Text(string key) => ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? throw new InvalidOperationException("The form permission text is missing.");
}
