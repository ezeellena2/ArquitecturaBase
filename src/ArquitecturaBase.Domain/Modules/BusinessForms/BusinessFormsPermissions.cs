namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public static class BusinessFormsPermissions
{
    public const string Read = "formtemplates.read";
    public const string Manage = "formtemplates.manage";
    public const string Publish = "formtemplates.publish";
    public const string RecordsCreate = "formrecords.create";
    public const string RecordsExport = "formrecords.export";
    public const string RecordsRead = "formrecords.read";
    /// <summary>Corrects and voids any response, whatever its approval; whoever loaded one does so only before a decision.</summary>
    public const string RecordsAmend = "formrecords.amend";
    public const string CatalogsRead = "formcatalogs.read";
    public const string CatalogsManage = "formcatalogs.manage";
    public const string RulesManage = "formrules.manage";
    public const string SuggestionsManage = "formsuggestions.manage";
    public const string ApprovalsDecide = "formapprovals.decide";
    public const string PersonDataManage = "formpersondata.manage";
    public static IReadOnlyCollection<string> All { get; } = [Read, Manage, Publish, RecordsCreate, RecordsRead, RecordsExport, RecordsAmend, CatalogsRead, CatalogsManage, RulesManage, ApprovalsDecide, SuggestionsManage, PersonDataManage];
}
