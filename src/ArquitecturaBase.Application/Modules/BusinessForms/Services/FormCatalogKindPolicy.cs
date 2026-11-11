namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Which catalogs offer their own items: a Manual one and an External one, which its source keeps. A Filters catalog offers
/// people. Only a Manual one has fields, so matching, presentation and choosing first by a field stay Manual.
/// </summary>
internal static class FormCatalogKindPolicy
{
    public const string External = "External";

    public static bool HasItems(string kind) => kind is "Manual" or External;
}
