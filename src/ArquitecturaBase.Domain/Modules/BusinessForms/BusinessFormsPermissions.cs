namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public static class BusinessFormsPermissions
{
    public const string Read = "formtemplates.read";
    public const string Manage = "formtemplates.manage";
    public const string Publish = "formtemplates.publish";
    public const string RecordsCreate = "formrecords.create";
    public const string RecordsExport = "formrecords.export";
    public const string RecordsRead = "formrecords.read";
    public const string CatalogsRead = "formcatalogs.read";
    public const string CatalogsManage = "formcatalogs.manage";
    public const string RulesManage = "formrules.manage";
    public static IReadOnlyCollection<string> All { get; } = [Read, Manage, Publish, RecordsCreate, RecordsRead, RecordsExport, CatalogsRead, CatalogsManage, RulesManage];
}
