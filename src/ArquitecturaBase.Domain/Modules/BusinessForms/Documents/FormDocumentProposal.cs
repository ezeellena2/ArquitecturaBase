using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms.Documents;

/// <summary>
/// «Armar formulario con la IA» (DOC6): what a reviewer who designs forms asked for, from one unclassified document or from a group of
/// similar ones (a topic of «Formularios que faltan»): the request of its proposal and the context the engine reads it with (who asked, in
/// which language and zone), the documents it was built from, in the order they were attached; the draft «Crear borrador» created from it
/// and the proposal it came from, once; and, once that form is published, the version its documents are read again for, with the ones still
/// to read and until when they wait. The actor and the request are unique.
/// </summary>
public sealed class FormDocumentProposal : Entity, IAuditable
{
    /// <summary>The most documents a request is built from and reads again once its form is published.</summary>
    public const int MaxDocuments = 20;
    public const int MaxTimeZoneIdLength = Users.AccountRules.TimeZoneIdMaxLength;

    private FormDocumentProposal() { }

    public Guid RequestId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Culture { get; private set; } = "es";
    public string TimeZoneId { get; private set; } = "";
    public DateTime RequestedAtUtc { get; private set; }
    /// <summary>The topic of the group it was asked for; null when it was asked for one document.</summary>
    public Guid? TopicId { get; private set; }
    public Guid[] DocumentIds { get; private set; } = [];
    /// <summary>The draft «Crear borrador» created from its proposal.</summary>
    public Guid? DefinitionId { get; private set; }
    /// <summary>The assistant's proposal that draft was created from.</summary>
    public Guid? ProposalId { get; private set; }
    /// <summary>The version of its form its documents were last read again for.</summary>
    public Guid? RereadVersionId { get; private set; }
    /// <summary>Its documents still to read again for <see cref="RereadVersionId"/>: each one leaves once its reading is asked or it no longer needs one.</summary>
    public Guid[] PendingRereadIds { get; private set; } = [];
    /// <summary>Until when the pending ones wait: the round that holds them, or an engine that cannot take them now.</summary>
    public DateTime? RereadAfterUtc { get; private set; }
    /// <summary>The UTC day its re-reads were last counted on, and how many it took that day: its part of the asker's daily share.</summary>
    public DateOnly? RereadDay { get; private set; }
    public int RereadsOnDay { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    /// <summary>A request of one reviewer, in Spanish or English, with its zone, its group's topic (or none) and its distinct documents.</summary>
    public static Result<FormDocumentProposal> Request(Guid requestId, Guid actorUserId, string culture, string timeZoneId, Guid? topicId,
        IReadOnlyList<Guid> documentIds, DateTime requestedAtUtc)
    {
        if (requestId == Guid.Empty || actorUserId == Guid.Empty || culture is not ("es" or "en") || string.IsNullOrWhiteSpace(timeZoneId)
            || timeZoneId.Length > MaxTimeZoneIdLength || !StorableText.IsStorable(timeZoneId) || requestedAtUtc.Kind != DateTimeKind.Utc
            || topicId == Guid.Empty || documentIds is not { Count: >= 1 and <= MaxDocuments } || documentIds.Contains(Guid.Empty)
            || documentIds.Distinct().Count() != documentIds.Count)
            return FormReceivedDocumentErrors.ContentInvalid;
        return new FormDocumentProposal
        {
            RequestId = requestId, ActorUserId = actorUserId, Culture = culture, TimeZoneId = timeZoneId, RequestedAtUtc = requestedAtUtc,
            TopicId = topicId, DocumentIds = [.. documentIds],
        };
    }

    /// <summary>Whether a repeated request asks for the same thing: the same group, by its topic, or the same single document.</summary>
    public bool Asks(Guid? documentId, Guid? topicId) => Asks(TopicId, DocumentIds, documentId, topicId);

    /// <summary>The same rule over a request as it was kept.</summary>
    public static bool Asks(Guid? keptTopicId, IReadOnlyList<Guid> keptDocumentIds, Guid? documentId, Guid? topicId) => topicId is { } topic
        ? keptTopicId == topic && documentId is null
        : keptTopicId is null && documentId is { } document && keptDocumentIds is [var only] && only == document;

    /// <summary>The draft created from its proposal and that proposal, once; the same pair again changes nothing.</summary>
    public Result Draft(Guid definitionId, Guid proposalId)
    {
        if (definitionId == Guid.Empty || proposalId == Guid.Empty) return FormReceivedDocumentErrors.ContentInvalid;
        if (DefinitionId is { } created)
            return created == definitionId && ProposalId == proposalId ? Result.Success() : FormReceivedDocumentErrors.RequestConflict;
        DefinitionId = definitionId;
        ProposalId = proposalId;
        return Result.Success();
    }

    /// <summary>Whether its documents were already read again for that published version of its form.</summary>
    public bool RereadsFor(Guid versionId) => RereadVersionId == versionId;

    /// <summary>
    /// Its documents are read again for a published version of its draft, once per version and only once nothing holds the ones still
    /// pending (a round, or an engine that cannot take them now), so two rounds of it never run at once: all of them wait, held until
    /// <paramref name="holdUntilUtc"/> by the round that starts.
    /// </summary>
    public Result StartReread(Guid versionId, DateTime nowUtc, DateTime holdUntilUtc)
    {
        if (versionId == Guid.Empty || nowUtc.Kind != DateTimeKind.Utc || holdUntilUtc.Kind != DateTimeKind.Utc || holdUntilUtc <= nowUtc)
            return FormReceivedDocumentErrors.ContentInvalid;
        if (DefinitionId is null || RereadsFor(versionId) || RereadHeld(nowUtc)) return FormReceivedDocumentErrors.StateConflict;
        RereadVersionId = versionId;
        PendingRereadIds = [.. DocumentIds];
        RereadAfterUtc = holdUntilUtc;
        return Result.Success();
    }

    /// <summary>Whether something holds the pending ones at <paramref name="nowUtc"/>: the round that took them, or an engine that cannot take them now.</summary>
    public bool RereadHeld(DateTime nowUtc) => PendingRereadIds.Length > 0 && RereadAfterUtc > nowUtc;

    /// <summary>Whether a round may take the pending ones at <paramref name="nowUtc"/>: some wait, and nothing holds them.</summary>
    public bool RereadDue(DateTime nowUtc) => PendingRereadIds.Length > 0 && !RereadHeld(nowUtc);

    /// <summary>A later round holds the pending ones until <paramref name="holdUntilUtc"/>: another host leaves them alone meanwhile.</summary>
    public Result ClaimReread(DateTime nowUtc, DateTime holdUntilUtc)
    {
        if (holdUntilUtc.Kind != DateTimeKind.Utc || holdUntilUtc <= nowUtc) return FormReceivedDocumentErrors.ContentInvalid;
        if (!RereadDue(nowUtc)) return FormReceivedDocumentErrors.StateConflict;
        RereadAfterUtc = holdUntilUtc;
        return Result.Success();
    }

    /// <summary>A document whose reading was asked, or that no longer needs one, leaves the pending ones; with none left nothing waits.</summary>
    public void Reread(Guid documentId)
    {
        PendingRereadIds = [.. PendingRereadIds.Where(id => id != documentId)];
        if (PendingRereadIds.Length == 0) RereadAfterUtc = null;
    }

    /// <summary>How many re-reads it took on <paramref name="day"/>.</summary>
    public int RereadsOn(DateOnly day) => RereadDay == day ? RereadsOnDay : 0;

    /// <summary>A round takes <paramref name="count"/> re-reads of <paramref name="day"/>; a new day starts from none.</summary>
    public Result CountRereads(DateOnly day, int count)
    {
        if (count < 0) return FormReceivedDocumentErrors.ContentInvalid;
        RereadsOnDay = RereadsOn(day) + count;
        RereadDay = day;
        return Result.Success();
    }

    /// <summary>The engine cannot take them now (its quota or its capacity): the pending ones wait until <paramref name="afterUtc"/>.</summary>
    public Result DeferReread(DateTime afterUtc)
    {
        if (afterUtc.Kind != DateTimeKind.Utc) return FormReceivedDocumentErrors.ContentInvalid;
        if (PendingRereadIds.Length > 0) RereadAfterUtc = afterUtc;
        return Result.Success();
    }

    public override string ToString() => "FormDocumentProposal { Content = [REDACTED] }";
}
