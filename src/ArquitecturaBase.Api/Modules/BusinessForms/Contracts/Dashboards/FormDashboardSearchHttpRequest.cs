namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts.Dashboards;
/// <summary>
/// The dashboard of a form (DOC7-A): the records search body of the sheet, under <c>filter</c>, and the period of its series,
/// <c>day</c>, <c>week</c> or <c>month</c>. The page, the size and the order of the filter do not change what it adds up.
/// </summary>
public sealed record FormDashboardSearchHttpRequest(FormRecordsSearchHttpRequest Filter, string Period)
{
    public override string ToString() => "FormDashboardSearchHttpRequest { Content = [REDACTED] }";
}
