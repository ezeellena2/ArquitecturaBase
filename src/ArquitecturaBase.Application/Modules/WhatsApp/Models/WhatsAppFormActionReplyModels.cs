namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>A tap on a form action button not yet handled, with the contact that sent it as it is now.</summary>
public sealed record WhatsAppFormActionTapRow(Guid Id, string WaMessageId, Guid ContactId, string ReplyId,
    Guid? ContactUserId, long ContactLinkRevision, string? ContactWaId)
{
    public override string ToString() => "WhatsAppFormActionTapRow { Content = [REDACTED] }";
}

/// <summary>The form action a button names: its run, its fixed recipient and the buttons sent (the stored quick replies).</summary>
public sealed record WhatsAppFormActionReplyTargetRow(Guid OutgoingId, Guid ContactId, Guid RunId, Guid RecipientUserId,
    long? RecipientLinkRevision, string? RecipientWaId, string QuickRepliesJson)
{
    public override string ToString() => "WhatsAppFormActionReplyTargetRow { Target = [REDACTED] }";
}

/// <summary>What the owner receives: the run, the person who pressed the button and the choice.</summary>
public sealed record WhatsAppFormActionReply(Guid RunId, Guid RecipientUserId, string ChoiceId)
{
    public override string ToString() => "WhatsAppFormActionReply { Target = [REDACTED] }";
}
