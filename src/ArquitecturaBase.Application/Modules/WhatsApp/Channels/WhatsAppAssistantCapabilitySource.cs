using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Channels;

internal sealed class WhatsAppAssistantCapabilitySource(IWhatsAppAssistantReader reader) : IAssistantCapabilitySource
{
    public const string OffersId="whatsapp.offered-forms";
    public const string NoReplyId="whatsapp.no-reply";
    public const string DeclineId="whatsapp.decline-form";
    private const string OffersInput="""{"type":"object","properties":{"definitionIds":{"type":"array","maxItems":100,"items":{"type":"string","format":"uuid"}}},"required":["definitionIds"],"additionalProperties":false}""";
    private const string NoReplySchema="{\"type\":\"object\",\"properties\":{\"reason\":{\"type\":\"string\",\"enum\":[\"not_form\",\"operator_needed\"]}},\"required\":[\"reason\"],\"additionalProperties\":false}";
    private const string DeclineInput="{\"type\":\"object\",\"properties\":{\"requestId\":{\"type\":\"string\",\"format\":\"uuid\"},\"definitionId\":{\"type\":\"string\",\"format\":\"uuid\"}},\"required\":[\"requestId\",\"definitionId\"],\"additionalProperties\":false}";
    private const string DeclineOutput="{\"type\":\"object\",\"properties\":{\"readOrdinal\":{\"type\":\"integer\",\"minimum\":1},\"requestId\":{\"type\":\"string\",\"format\":\"uuid\"},\"definitionId\":{\"type\":\"string\",\"format\":\"uuid\"}},\"required\":[\"readOrdinal\",\"requestId\",\"definitionId\"],\"additionalProperties\":false}";
    private const string OffersOutput="""{"type":"object","properties":{"subjectUserId":{"type":"string","format":"uuid"},"culture":{"type":"string","enum":["es","en"]},"declinedDefinitionIds":{"type":"array","maxItems":100,"uniqueItems":true,"items":{"type":"string","format":"uuid"}},"offers":{"type":"array","maxItems":20,"items":{"type":"object","properties":{"requestId":{"type":"string","format":"uuid"},"definitionId":{"type":"string","format":"uuid"},"versionId":{"type":"string","format":"uuid"},"name":{"type":"string","maxLength":120}},"required":["requestId","definitionId","versionId","name"],"additionalProperties":false}}},"required":["subjectUserId","culture","offers","declinedDefinitionIds"],"additionalProperties":false}""";
    private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=false,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=8};
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions {get;}=[
        new(OffersId,1,AssistantCapabilityKind.Read,"After reading form candidates, pass their definitionIds to read this recipient's durable declines and recent factual offers. Declines cover only those selected IDs and may predate the recent offers. A declined or quota-blocked candidate still exists; never treat it as a missing form.",OffersInput,OffersOutput,new([],[]),new(8192,16384,120,5)),
        new(NoReplyId,1,AssistantCapabilityKind.Proposal,"Leave the conversation for an operator or ordinary conversation without sending a free model message or marking it read.",NoReplySchema,NoReplySchema,new([],[]),new(1024,1024,1,2)),
        new(DeclineId,1,AssistantCapabilityKind.Proposal,"Record that the recipient declined one actually observed prior offer. Select references only from the latest offers read of this turn.",DeclineInput,DeclineOutput,new([],[]),new(1024,1024,1,2))];

    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request,CancellationToken ct)
    {
        if(request.TurnId==Guid.Empty || request.Context.SubjectUserId is not {} subject || subject==Guid.Empty || request.Capability.Version!=1 || request.Context.Culture is not ("es" or "en")) return WhatsAppAssistantErrors.InvalidProposal;
        try
        {
            using var document=JsonDocument.Parse(request.ArgumentsJson,new JsonDocumentOptions{MaxDepth=4});
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object || root.EnumerateObject().Select(property=>property.Name).Distinct(StringComparer.Ordinal).Count()!=root.EnumerateObject().Count()) return WhatsAppAssistantErrors.InvalidProposal;
            if(request.Capability.Id==OffersId)
            {
                var offerInput = JsonSerializer.Deserialize<OffersArguments>(request.ArgumentsJson, Options);
                if (offerInput?.DefinitionIds is not { Count: <= 100 } ids || ids.Any(id => id == Guid.Empty)
                    || ids.Distinct().Count() != ids.Count) return WhatsAppAssistantErrors.InvalidProposal;
                var declined = await reader.ListDeclinedDefinitionIdsAsync(subject, ids, ct);
                var offers = await reader.ListOffersAsync(subject, ct);
                var payload = BoundOffers(subject, request.Context.Culture, offers, declined);
                return ValidOffers(payload) ? Response(payload, payload.Offers.Count + payload.DeclinedDefinitionIds.Count) : WhatsAppAssistantErrors.InvalidProposal;
            }
            if(request.Capability.Id==NoReplyId)
            {
                var payload=JsonSerializer.Deserialize<WhatsAppAssistantNoReplyPayload>(request.ArgumentsJson,Options);
                return payload is {Reason:"not_form" or "operator_needed"} ? Response(payload,1) : WhatsAppAssistantErrors.InvalidProposal;
            }
            if(request.Capability.Id!=DeclineId) return WhatsAppAssistantErrors.InvalidProposal;
            var input=JsonSerializer.Deserialize<DeclineArguments>(request.ArgumentsJson,Options);
            var read=request.ObservedReads.Where(item=>item.TurnId==request.TurnId && item.Ordinal>=1 && item.Capability==new AssistantCapabilityReference(OffersId,1))
                .OrderByDescending(item=>item.Ordinal).FirstOrDefault();
            if(input is null || read?.Payload is not StructuredFormOffersPayload observed || observed.SubjectUserId!=subject || observed.Culture!=request.Context.Culture || !ValidOffers(observed)
                || !observed.Offers.Any(offer=>offer.RequestId==input.RequestId && offer.DefinitionId==input.DefinitionId)) return WhatsAppAssistantErrors.InvalidProposal;
            return Response(new WhatsAppAssistantDeclinePayload(read.Ordinal,input.RequestId,input.DefinitionId),1);
        }
        catch(JsonException){return WhatsAppAssistantErrors.InvalidProposal;}
    }

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability,string outputJson)
    {
        if(capability.Version!=1) return WhatsAppAssistantErrors.InvalidProposal;
        try
        {
            AssistantCapabilityPayload? value=capability.Id switch
            {
                OffersId=>JsonSerializer.Deserialize<StructuredFormOffersPayload>(outputJson,Options),
                NoReplyId=>JsonSerializer.Deserialize<WhatsAppAssistantNoReplyPayload>(outputJson,Options),
                DeclineId=>JsonSerializer.Deserialize<WhatsAppAssistantDeclinePayload>(outputJson,Options),
                _=>null,
            };
            return value switch
            {
                StructuredFormOffersPayload offers when ValidOffers(offers)=>offers,
                WhatsAppAssistantNoReplyPayload {Reason:"not_form" or "operator_needed"} noReply=>noReply,
                WhatsAppAssistantDeclinePayload decline when decline.ReadOrdinal>=1 && decline.RequestId!=Guid.Empty && decline.DefinitionId!=Guid.Empty=>decline,
                _=>WhatsAppAssistantErrors.InvalidProposal,
            };
        }
        catch(JsonException){return WhatsAppAssistantErrors.InvalidProposal;}
    }
    private static bool ValidOffers(StructuredFormOffersPayload value)=>value.SubjectUserId!=Guid.Empty && value.Culture is "es" or "en" && value.Offers is {Count:<=20} && value.DeclinedDefinitionIds is {Count:<=100} && value.DeclinedDefinitionIds.All(id=>id!=Guid.Empty) && value.DeclinedDefinitionIds.Distinct().Count()==value.DeclinedDefinitionIds.Count
        && value.Offers.All(offer=>offer.RequestId!=Guid.Empty && offer.DefinitionId!=Guid.Empty && offer.VersionId!=Guid.Empty && !string.IsNullOrWhiteSpace(offer.Name) && offer.Name.Length<=120)
        && value.Offers.Select(offer=>(offer.RequestId,offer.DefinitionId)).Distinct().Count()==value.Offers.Count;
    private static StructuredFormOffersPayload BoundOffers(Guid subject, string culture,
        IReadOnlyList<StructuredFormOfferReference> offers, IReadOnlyList<Guid> declined)
    {
        var payload = new StructuredFormOffersPayload(subject, culture, [], declined);
        foreach (var offer in offers.Take(20))
        {
            var next = payload with { Offers = [.. payload.Offers, offer] };
            if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { state = "complete", payload = next }, Options)) > 16384) break;
            payload = next;
        }
        return payload;
    }
    private sealed record OffersArguments(IReadOnlyList<Guid> DefinitionIds);
    private static AssistantCapabilityResponse Response(AssistantCapabilityPayload payload,int count)=>new("complete",payload,[],count);
    private sealed record DeclineArguments(Guid RequestId,Guid DefinitionId);
}
