namespace ArquitecturaBase.Api.Modules.WhatsApp.Contracts;

public sealed record SendWhatsAppFormHttpRequest(Guid RequestId, Guid DefinitionId, Guid VersionId,
    long ExpectedModeRevision, string? ExpectedRecipientContextRevision, string? Message)
{
    public override string ToString() => "SendWhatsAppFormHttpRequest { Content and Context = [REDACTED] }";
}
