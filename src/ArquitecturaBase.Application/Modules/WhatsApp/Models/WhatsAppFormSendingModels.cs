using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.ValueObjects;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;
public sealed record WhatsAppFormPreparationResponse(string Culture, string RecipientContextRevision,
    StructuredFormPreparationResponse Preparation);
public sealed record SendWhatsAppFormRequest(Guid ContactId, Guid RequestId, Guid DefinitionId, Guid VersionId,
    long ExpectedModeRevision, string? ExpectedRecipientContextRevision, string? Message)
{
    public override string ToString() => "SendWhatsAppFormRequest { Content and Context = [REDACTED] }";
}
public sealed record WhatsAppFormPresentationRow(Guid DefinitionId, Guid VersionId, int VersionNumber, string NameJson, string Culture)
{
    public override string ToString() => "WhatsAppFormPresentationRow { Name = [REDACTED] }";
}
public sealed record WhatsAppFormPresentationResponse(Guid DefinitionId, Guid VersionId, int VersionNumber, IReadOnlyDictionary<string, string> Name, string Culture)
{
    public override string ToString() => "WhatsAppFormPresentationResponse { Name = [REDACTED] }";
}
public sealed record WhatsAppFlowSessionPayload(int Format, Guid ContactId, Guid UserId, Guid OperatorUserId,
    Guid DefinitionId, Guid VersionId, Guid BindingId, string Culture, string RecipientRevision, long ModeRevision,
    string MetaFlowId, string TechnicalName, string ExpectedHash, string Token, string ButtonText, string StartScreen,
    WhatsAppFlowAccountContext Account, IReadOnlyList<StructuredFormCatalogOffer>? Catalogs = null)
{
    public override string ToString() => "WhatsAppFlowSessionPayload { Content = [REDACTED] }";
}
public sealed record WhatsAppFormBindingRow(Guid BindingId, Guid OperationId, Guid NeutralOperationId, Guid DefinitionId,
    Guid VersionId, string Culture, string TechnicalName, string MetaFlowId, byte[] ProtectedProjection, byte[] ProtectedManifest)
{
    public override string ToString() => "WhatsAppFormBindingRow { Content = [REDACTED] }";
}
public sealed record WhatsAppFormDispatchRow(Guid OutgoingId, Guid SessionId, string TokenHash, Guid ContactId, string? WaId, Guid UserId, Guid OperatorUserId,
    Guid DefinitionId, Guid VersionId, Guid BindingId, string Culture, byte[] ProtectedPayload, DateTime ExpiresAtUtc,
    WhatsAppOutgoingMessageState State, string? Body, WhatsAppConversationMode Mode, long ModeRevision, DateTime LastInboundAtUtc)
{
    public override string ToString() => "WhatsAppFormDispatchRow { Content = [REDACTED] }";
}

public sealed record WhatsAppFlowReplySessionRow(Guid SessionId, Guid ContactId, Guid UserId, Guid DefinitionId,
    Guid VersionId, Guid BindingId, Guid OperationId, string Culture, byte[] ProtectedProjection, string Prefix,
    string TokenHash, byte[] ProtectedPayload, DateTime ExpiresAtUtc, bool IsCurrentActiveRecipient,
    WhatsAppFlowCaptureState CaptureState, string? CaptureErrorCode, string? RejectedQuestion = null)
{
    public override string ToString() => "WhatsAppFlowReplySessionRow { Content = [REDACTED] }";
}

public sealed record WhatsAppPendingFlowReplyRow(string WaMessageId, Guid ContactId, DateTime OccurredAtUtc,
    byte[] ProtectedResponse);

public sealed record WhatsAppFlowMessageResponse : WhatsAppOutboundMessage
{
    public WhatsAppFlowMessageResponse(PhoneNumber to, string body, string buttonText, string flowId, string flowToken,
        string startScreen, WhatsAppFlowAccountContext account, IReadOnlyList<StructuredFormCatalogOffer>? catalogs = null) : base(to)
    {
        Body = WhatsAppMessageLimits.Require(body, MaxInteractiveBodyLength, nameof(body));
        if (!WhatsAppOutgoingMessage.IsVisibleBody(Body))
            throw new ArgumentException("The Flow message body must contain visible, storable text.", nameof(body));
        ButtonText = WhatsAppMessageLimits.Require(buttonText, 20, nameof(buttonText));
        if (string.IsNullOrWhiteSpace(flowId) || !flowId.All(char.IsAsciiDigit))
            throw new ArgumentException("The provider Flow id must be numeric.", nameof(flowId));
        FlowId = flowId;
        FlowToken = WhatsAppMessageLimits.Require(flowToken, 128, nameof(flowToken));
        StartScreen = WhatsAppMessageLimits.Require(startScreen, 64, nameof(startScreen));
        Account = account ?? throw new ArgumentNullException(nameof(account));
        Catalogs = catalogs ?? [];
    }

    public string Body { get; }
    public string ButtonText { get; }
    public string FlowId { get; }
    public string FlowToken { get; }
    public string StartScreen { get; }
    public WhatsAppFlowAccountContext Account { get; }
    public IReadOnlyList<StructuredFormCatalogOffer> Catalogs { get; }
    public override WhatsAppMessagePurpose Purpose => WhatsAppMessagePurpose.Business;
    public override string SafeSummary => $"{Body} [{ButtonText}]";
}
