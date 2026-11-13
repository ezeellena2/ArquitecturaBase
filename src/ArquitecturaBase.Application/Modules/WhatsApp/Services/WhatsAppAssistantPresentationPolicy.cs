using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;
internal sealed class WhatsAppAssistantPresentationPolicy(IWhatsAppFlowSessionProtector protector,IWhatsAppAssistantReader references,
    IStructuredRecordSource records,IPermissionService permissions,TimeProvider clock,IStructuredFormSource forms)
{
    /// <summary>
    /// Review of task 9: the names the channel's forms have now in the reader's language, by id, to name the forms of «Por qué lo propuso» and
    /// «Por qué preguntó» when the chat is read.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid,string>> FormNamesAsync(string culture,CancellationToken ct)=>
        (await forms.ListAssistantFormsAsync(culture,ct)).GroupBy(form=>form.DefinitionId).ToDictionary(group=>group.Key,group=>group.First().Name);
    public WhatsAppAssistantExplanationResponse? Explain(WhatsAppAssistantPresentationRow? row,bool canReadRecords)
    {
        if(!canReadRecords || row is null || row.ContentExpiresAtUtc<=clock.GetUtcNow().UtcDateTime)return null;
        var content=protector.ReadAssistant(row.SessionId,row.ContactId,row.UserId,row.VersionId,row.ProtectedContent);
        return content.Origin.Reason is {Length:>0} reason ? new(reason,content.ProposedFields) : null;
    }
    /// <summary>
    /// Task 9 of the documents: what the form of a received document went with: how many data were read with doubts and, only with
    /// <c>formrecords.read</c>, the values read; nothing once the sealed content expired.
    /// </summary>
    public (int Doubtful, IReadOnlyList<WhatsAppAssistantPrefillResponse>? Fields)? ExplainDocument(WhatsAppAssistantPresentationRow? row,bool canReadRecords)
    {
        if(row is null || row.ContentExpiresAtUtc<=clock.GetUtcNow().UtcDateTime)return null;
        var content=protector.ReadAssistant(row.SessionId,row.ContactId,row.UserId,row.VersionId,row.ProtectedContent);
        if(content.Origin.Document is null)return null;
        return (content.Server?.Marks?.Count(mark=>mark.Kind==ArquitecturaBase.Application.Models.StructuredData.StructuredDocumentMarkKind.Doubtful)??0,
            canReadRecords?content.ProposedFields:null);
    }
    /// <summary>
    /// W3 (task 24): «Prellenado con» of a resend, with the privacy of «Por qué lo mandó»: only with <c>formrecords.read</c> and while the
    /// retention keeps what was written. It is what the send had taken up to <paramref name="untilUtc"/> (a resend that went out carried
    /// what was taken before it left; a queued one carries everything), the location before the contact, from one to three values. What a
    /// send took keeps the clock's instant and the messages the database's (1 µs), so it is compared at that precision. What does not
    /// read is left out, never guessed.
    /// </summary>
    public IReadOnlyList<WhatsAppSharedPrefillResponse>? Prefill(WhatsAppSharedPrefillRow? row,bool canReadRecords,DateTime? untilUtc)
    {
        if(!canReadRecords || row is null)return null;
        try
        {
            var shared=WhatsAppFlowSession.ReadSharedContent(row.SharedContentJson);
            var written=protector.ReadShared(row.SessionId,row.ContactId,row.UserId,row.VersionId,row.ProtectedValues).Values;
            WhatsAppSharedPrefillResponse[] prefill=[.. new[]{shared.Location,shared.Contact}.OfType<WhatsAppFlowSharedTake>()
                .Where(take=>untilUtc is null || take.AtUtc.AddTicks(-(take.AtUtc.Ticks%TimeSpan.TicksPerMicrosecond))<=untilUtc).SelectMany(take=>take.FieldIds)
                .Select(fieldId=>written.FirstOrDefault(value=>value.FieldId==fieldId)).OfType<WhatsAppFlowSharedValue>()
                .Where(value=>value.Label is {Length:>0} && value.Value is {Length:>0}).Select(value=>new WhatsAppSharedPrefillResponse(value.Label,value.Value))];
            // A location fills one question and a contact two: a resend says one to three.
            return prefill.Length is >= 1 and <= 3 ? prefill : null;
        }
        catch(Exception exception) when(exception is CryptographicException or InvalidOperationException or JsonException){return null;}
    }
    public async Task<WhatsAppAssistantExplanationResponse?> ReadAsync(Guid actor,WhatsAppAssistantPresentationRow? row,CancellationToken ct)
    {
        if(row is null || row.ContentExpiresAtUtc<=clock.GetUtcNow().UtcDateTime)return null;
        var access=await records.ReadAsync(actor,CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="en"?"en":"es",[],ct);
        return Explain(row,access.CanReadRecords);
    }
    public async Task<WhatsAppAssistantSuggestionPointerResponse?> FindCurrentAsync(Guid contactId,Guid actor,CancellationToken ct)
    {
        if(!await permissions.HasPermissionAsync(actor,WhatsAppPermissions.Read,ct)
            || !await permissions.HasPermissionAsync(actor,WhatsAppPermissions.Send,ct))return null;
        var pointer=await references.FindCurrentAsync(contactId,ct);
        return await permissions.HasPermissionAsync(actor,WhatsAppPermissions.Send,ct)?pointer:null;
    }
    public static IReadOnlyList<string>? Titles(string? json,bool outgoing)
    {
        if(json is null)return null;
        return outgoing ? JsonSerializer.Deserialize<WhatsAppOutgoingQuickReply[]>(json,JsonSerializerOptions.Web)?.Select(reply=>reply.Title).ToArray()
            : JsonSerializer.Deserialize<string[]>(json,JsonSerializerOptions.Web);
    }
    public static WhatsAppAssistantPresentationRow? Row(WhatsAppFlowSession session)=>
        session.ProtectedAssistantContent is {} content && session.AssistantContentExpiresAtUtc is {} expiry
            ?new(session.Id,session.ContactId,session.UserId,session.VersionId,content,expiry):null;
}
