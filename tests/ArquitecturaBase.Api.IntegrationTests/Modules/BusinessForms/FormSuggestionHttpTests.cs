using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Suggestion_routes_require_only_suggestion_management_and_an_authenticated_session()
    {
        using var anonymous=factory.CreateClient();using var designer=await OperatorAsync([BusinessFormsPermissions.Read,BusinessFormsPermissions.Manage]);
        using var manager=await OperatorAsync([BusinessFormsPermissions.SuggestionsManage]);var id=Guid.NewGuid();
        foreach(var suffix in new[]{"","/options","/revisions","/revisions/1"})
        {
            var route=$"/api/forms/definitions/{id}/suggestions"+suffix;
            using var missing=await anonymous.GetAsync(route,Ct);Assert.Equal(HttpStatusCode.Unauthorized,missing.StatusCode);
            using var denied=await designer.GetAsync(route,Ct);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            using var allowed=await manager.GetAsync(route,Ct);Assert.Equal(HttpStatusCode.NotFound,allowed.StatusCode);
        }
        using var missingSave=await anonymous.PostAsJsonAsync($"/api/forms/definitions/{id}/suggestions",new{expectedRevision=0,suggestions=Array.Empty<object>()},Ct);
        using var deniedSave=await designer.PostAsJsonAsync($"/api/forms/definitions/{id}/suggestions",new{expectedRevision=0,suggestions=Array.Empty<object>()},Ct);
        Assert.Equal(HttpStatusCode.Unauthorized,missingSave.StatusCode);Assert.Equal(HttpStatusCode.Forbidden,deniedSave.StatusCode);
    }
    [Fact]
    public async Task Suggestion_revisions_keep_their_own_history_and_accept_a_published_destination_without_general_offer_instructions()
    {
        using var author=await OperatorAsync([BusinessFormsPermissions.Read,BusinessFormsPermissions.Manage,BusinessFormsPermissions.Publish]);
        await using var host=PublicationHost();using var client=host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader,author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var source=await PublishSuggestionFormAsync(host,client,"Entrega sugerencias");var destination=await PublishSuggestionFormAsync(host,client,"Factura sugerencias");
        var draftOnly=await CreateAsync(client,Payload("Destino sin publicar"));var unpublishedId=draftOnly.GetProperty("definition").GetProperty("id").GetGuid();
        using var manager=await OperatorAsync([BusinessFormsPermissions.SuggestionsManage]);var route=$"/api/forms/definitions/{source.Id}/suggestions";
        using var empty=await manager.GetAsync(route,Ct);Assert.Equal(HttpStatusCode.OK,empty.StatusCode);Assert.Equal(0,(await empty.ReadJsonAsync()).GetProperty("revision").GetInt64());
        await FormSuggestionContractCapture.WriteAsync(empty,"empty-es",Ct);
        using var options=await manager.GetAsync(route+"/options",Ct);Assert.Equal(HttpStatusCode.OK,options.StatusCode);var optionsBody=await options.ReadJsonAsync();
        Assert.Equal(source.VersionId,optionsBody.GetProperty("source").GetProperty("versionId").GetGuid());
        Assert.Contains(optionsBody.GetProperty("destinations").EnumerateArray(),item=>item.GetProperty("definitionId").GetGuid()==destination.Id);
        Assert.DoesNotContain(optionsBody.GetProperty("destinations").EnumerateArray(),item=>item.GetProperty("definitionId").GetGuid()==unpublishedId);
        await FormSuggestionContractCapture.WriteAsync(options,"options-es",Ct);
        var field=destination.Saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[0].GetProperty("fieldId").GetGuid();var suggestionId=Guid.NewGuid();
        var body=new{expectedRevision=0,suggestions=new[]{new{id=suggestionId,destinationDefinitionId=destination.Id,mode="Assistant",when="Cuando pida la factura",isActive=true,
            conditions=Array.Empty<object>(),mappings=new[]{new{source=new{kind="RecordNumber"},destinationFieldId=field}}}}};
        using var saved=await manager.PostAsJsonAsync(route,body,Ct);Assert.Equal(HttpStatusCode.Created,saved.StatusCode);Assert.EndsWith("/revisions/1",saved.Headers.Location!.ToString(),StringComparison.Ordinal);
        var first=await saved.ReadJsonAsync();Assert.Equal(source.VersionId,first.GetProperty("sourceVersionId").GetGuid());Assert.Equal(destination.VersionId,first.GetProperty("suggestions")[0].GetProperty("destinationVersionId").GetGuid());
        Assert.Equal("Cuando pida la factura",first.GetProperty("suggestions")[0].GetProperty("when").GetString());await FormSuggestionContractCapture.WriteAsync(saved,"saved-es",Ct);
        using var stale=await manager.PostAsJsonAsync(route,body,Ct);Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        Assert.Equal(FormSuggestionErrors.RevisionConflict.Code,(await stale.ReadJsonAsync()).GetProperty("code").GetString());await FormSuggestionContractCapture.WriteAsync(stale,"conflict-es",Ct);
        using var disabled=await manager.PostAsJsonAsync(route,new{expectedRevision=1,suggestions=Array.Empty<object>()},Ct);Assert.Equal(HttpStatusCode.Created,disabled.StatusCode);
        using var history=await manager.GetAsync(route+"/revisions?pageSize=1",Ct);Assert.Equal(HttpStatusCode.OK,history.StatusCode);var page=await history.ReadJsonAsync();
        Assert.Equal(2,page.GetProperty("totalCount").GetInt32());Assert.Equal(1,page.GetProperty("items")[0].GetProperty("changes").GetProperty("removedSuggestions").GetInt32());
        await FormSuggestionContractCapture.WriteAsync(history,"history-es",Ct);
        using var previous=await manager.GetAsync(route+"/revisions/1",Ct);Assert.True(JsonNode.DeepEquals(JsonNode.Parse(first.GetRawText()),JsonNode.Parse((await previous.ReadJsonAsync()).GetRawText())));
        await FormSuggestionContractCapture.WriteAsync(previous,"revision-es",Ct);
        manager.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");using var english=await manager.GetAsync(route+"/options",Ct);await FormSuggestionContractCapture.WriteAsync(english,"options-en",Ct);
        await factory.ExecuteDbContextAsync(async db=>
        {
            var definition=await db.Set<BusinessFormDefinition>().SingleAsync(value=>value.Id==source.Id,Ct);Assert.Equal(1,definition.Revision);Assert.Equal(source.VersionId,definition.ActiveVersionId);Assert.Null(definition.ActiveRuleSetId);
            Assert.Null(await db.Set<BusinessFormDefinition>().Where(value=>value.Id==destination.Id).Select(value=>value.OfferWhen).SingleAsync(Ct));
            var sets=await db.Set<BusinessFormSuggestionSet>().Where(value=>value.DefinitionId==source.Id).OrderBy(value=>value.Revision).ToArrayAsync(Ct);
            Assert.Equal(2,sets.Length);Assert.Equal(sets[1].Id,definition.ActiveSuggestionSetId);Assert.Contains(suggestionId.ToString(),sets[0].SuggestionsJson,StringComparison.Ordinal);
            Assert.Equal(1,await db.Set<BusinessFormVersion>().CountAsync(value=>value.DefinitionId==source.Id,Ct));
        });
    }
    [Fact]
    public async Task Unpublished_sources_and_unknown_request_properties_do_not_create_suggestion_revisions()
    {
        using var designer=await AdminAsync();var created=await CreateAsync(designer,Payload("Sugerencias sin publicar"));var id=created.GetProperty("definition").GetProperty("id").GetGuid();
        using var manager=await OperatorAsync([BusinessFormsPermissions.SuggestionsManage]);var route=$"/api/forms/definitions/{id}/suggestions";
        using var options=await manager.GetAsync(route+"/options",Ct);Assert.Equal(HttpStatusCode.Conflict,options.StatusCode);Assert.Equal(FormSuggestionErrors.SourceNotPublished.Code,(await options.ReadJsonAsync()).GetProperty("code").GetString());
        await FormSuggestionContractCapture.WriteAsync(options,"source-unpublished-es",Ct);
        using var save=await manager.PostAsJsonAsync(route,new{expectedRevision=0,suggestions=Array.Empty<object>()},Ct);Assert.Equal(HttpStatusCode.Conflict,save.StatusCode);
        using var unknown=await manager.PostAsJsonAsync(route,new{expectedRevision=0,suggestions=Array.Empty<object>(),workflow="inventado"},Ct);Assert.Equal(HttpStatusCode.BadRequest,unknown.StatusCode);
        Assert.Equal(0,await factory.ExecuteDbContextAsync(db=>db.Set<BusinessFormSuggestionSet>().CountAsync(value=>value.DefinitionId==id,Ct)));
    }
    private static async Task<(Guid Id,Guid VersionId,JsonElement Saved)> PublishSuggestionFormAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host,HttpClient client,string name)
    {
        var saved=await CreateAsync(client,Payload(name));var id=saved.GetProperty("definition").GetProperty("id").GetGuid();var attempt=await PublicationAttemptAsync(client,id,saved);
        using var accepted=await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications",attempt,Ct);Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
        var version=await CompletePublicationAsync(host,(await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(),id);return(id,version,saved);
    }
}
