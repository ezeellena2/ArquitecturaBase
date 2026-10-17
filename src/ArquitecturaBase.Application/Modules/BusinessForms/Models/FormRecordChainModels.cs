namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormRecordChainStoredRow(Guid Id, Guid DefinitionId, Guid? OriginRecordId, string Prefix,
    long Number, string NameJson, DateTime SubmittedAtUtc)
{
    public override string ToString() => "FormRecordChainStoredRow { Content = [REDACTED] }";
}
public sealed record FormResponseChainRecordResponse(Guid Id, Guid DefinitionId, Guid? OriginRecordId, string Number,
    string Name, DateTime SubmittedAtUtc)
{
    public override string ToString() => "FormResponseChainRecordResponse { Content = [REDACTED] }";
}
