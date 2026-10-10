namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public static class BusinessFormsPermissions
{
    public const string Read = "formtemplates.read";
    public const string Manage = "formtemplates.manage";
    public const string Publish = "formtemplates.publish";
    public const string RecordsCreate = "formrecords.create";
    public const string RecordsRead = "formrecords.read";
    public static IReadOnlyCollection<string> All { get; } = [Read, Manage, Publish, RecordsCreate, RecordsRead];
}
