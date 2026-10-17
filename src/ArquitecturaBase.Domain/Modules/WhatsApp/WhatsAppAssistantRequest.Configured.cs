namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public sealed partial class WhatsAppAssistantRequest
{
    public Guid? SourceRecordId {get;private set;}
    public Guid? SuggestionSetId {get;private set;}
    public Guid? SuggestionId {get;private set;}
    public string? CapturedCulture {get;private set;}
    public bool IsAlways {get;private set;}

    public static WhatsAppAssistantRequest CreateConfiguredManual(Guid requestId,Guid contactId,Guid sourceMessageId,Guid subjectUserId,
        Guid sourceRecordId,Guid suggestionSetId,Guid suggestionId,string culture,long modeRevision,long linkRevision,DateTime createdAtUtc,DateTime contentExpiresAtUtc)
    {
        foreach(var id in new[]{requestId,contactId,sourceMessageId,subjectUserId,sourceRecordId,suggestionSetId,suggestionId})
            ArgumentOutOfRangeException.ThrowIfEqual(id,Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(modeRevision);ArgumentOutOfRangeException.ThrowIfNegative(linkRevision);
        if(culture is not ("es" or "en") || createdAtUtc.Kind!=DateTimeKind.Utc || contentExpiresAtUtc.Kind!=DateTimeKind.Utc || contentExpiresAtUtc<=createdAtUtc)
            throw new ArgumentException("The configured reference must have a captured culture and UTC lifetime.");
        return new(){RequestId=requestId,ContactId=contactId,SourceMessageId=sourceMessageId,SubjectUserId=subjectUserId,
            Kind=WhatsAppAssistantRequestKind.Manual,ModeRevision=modeRevision,LinkRevision=linkRevision,CreatedAtUtc=createdAtUtc,ContentExpiresAtUtc=contentExpiresAtUtc,
            SourceRecordId=sourceRecordId,SuggestionSetId=suggestionSetId,SuggestionId=suggestionId,CapturedCulture=culture,IsAlways=true};
    }
    public bool SetCapturedSuggestion(Guid sourceRecordId,Guid? suggestionSetId,Guid? suggestionId,string culture,bool always,DateTime nowUtc)
    {
        if(sourceRecordId==Guid.Empty || suggestionSetId==Guid.Empty || suggestionId==Guid.Empty
            || suggestionId.HasValue!=suggestionSetId.HasValue || always && !suggestionId.HasValue || culture is not ("es" or "en")) return false;
        if(SourceRecordId.HasValue && SourceRecordId!=sourceRecordId || CapturedCulture is not null && CapturedCulture!=culture) return false;
        if(SuggestionId.HasValue) return SuggestionSetId==suggestionSetId && SuggestionId==suggestionId && IsAlways==always;
        if(State!=WhatsAppAssistantRequestState.Pending || nowUtc>=ContentExpiresAtUtc) return false;
        SourceRecordId=sourceRecordId;SuggestionSetId=suggestionSetId;SuggestionId=suggestionId;CapturedCulture=culture;IsAlways=always;
        return true;
    }
}
