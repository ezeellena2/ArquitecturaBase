namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

public sealed record FormPublicationReceiptHttpResponse(Guid RequestId, Guid OperationId, Guid DefinitionId, Guid VersionId,
    long VersionNumber, long DraftRevision, string State, DateTime AcceptedAtUtc, string RecoveryPath);
