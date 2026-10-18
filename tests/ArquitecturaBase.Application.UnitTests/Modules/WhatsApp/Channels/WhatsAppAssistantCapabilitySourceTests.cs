using ArquitecturaBase.Application.Models.StructuredData;
using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Channels;

public sealed class WhatsAppAssistantCapabilitySourceTests
{
    private static readonly Guid Subject = Guid.NewGuid();
    private static readonly Guid Turn = Guid.NewGuid();
    private static AssistantCapabilityRequest Request(string id,string json,IReadOnlyList<AssistantObservedRead>? reads=null,Guid? turn=null) =>
        new(turn??Turn,new("whatsapp-assistant",Subject,Subject,"es","America/Argentina/Buenos_Aires",new([],[]),[],[]),new(id,1),json,reads??[]);

    [Fact]
    public void The_three_capabilities_declare_precise_read_and_proposal_contracts_without_a_global_create_permission()
    {
        var source=new WhatsAppAssistantCapabilitySource(new FakeWhatsAppAssistantReader());
        Assert.Equal(3,source.Definitions.Count);
        Assert.All(source.Definitions,definition=>Assert.Empty(definition.RequiredPermissions.Subject));
        Assert.Equal(AssistantCapabilityKind.Read,source.Definitions.Single(definition=>definition.Id==WhatsAppAssistantCapabilitySource.OffersId).Kind);
    }

    [Fact]
    public async Task Offers_are_metadata_for_the_explicit_subject_and_roundtrip_through_the_owner_codec()
    {
        var reader=new FakeWhatsAppAssistantReader();
        reader.Offers.Add(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Visita ficticia"));
        var source=new WhatsAppAssistantCapabilitySource(reader);
        var result=await source.ExecuteAsync(Request(WhatsAppAssistantCapabilitySource.OffersId,"{\"definitionIds\":[]}"),TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var payload=Assert.IsType<StructuredFormOffersPayload>(result.Value.Payload);
        Assert.Equal(Subject,payload.SubjectUserId); Assert.Single(payload.Offers);
        var decoded=source.ParseOutput(new(WhatsAppAssistantCapabilitySource.OffersId,1),JsonSerializer.Serialize(payload,JsonSerializerOptions.Web));
        Assert.True(decoded.IsSuccess); Assert.IsType<StructuredFormOffersPayload>(decoded.Value);
    }

    [Fact]
    public async Task A_large_recent_history_is_bounded_instead_of_invalidating_the_offers_read()
    {
        var reader = new FakeWhatsAppAssistantReader();
        for (var index = 0; index < 101; index++)
            reader.Offers.Add(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('漢', 120)));
        var source = new WhatsAppAssistantCapabilitySource(reader);
        var result = await source.ExecuteAsync(Request(WhatsAppAssistantCapabilitySource.OffersId, "{\"definitionIds\":[]}"), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<StructuredFormOffersPayload>(result.Value.Payload);
        Assert.InRange(payload.Offers.Count, 1, 20);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { state = result.Value.State, payload }, JsonSerializerOptions.Web)) <= 16384);
    }

    [Fact]
    public async Task A_full_form_name_is_preserved_without_truncation()
    {
        var reader = new FakeWhatsAppAssistantReader();
        var name = new string('ñ', 120);
        reader.Offers.Add(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), name));
        var result = await new WhatsAppAssistantCapabilitySource(reader).ExecuteAsync(Request(WhatsAppAssistantCapabilitySource.OffersId,
            "{\"definitionIds\":[]}"), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(name, Assert.Single(Assert.IsType<StructuredFormOffersPayload>(result.Value.Payload).Offers).Name);
    }

    [Fact]
    public async Task Declines_are_a_definition_set_for_requested_candidates_even_without_any_recent_offer()
    {
        var reader = new FakeWhatsAppAssistantReader();
        var definitions = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var definition in definitions) reader.DeclinedDefinitions.Add(definition);
        var unrelated = Guid.NewGuid(); reader.DeclinedDefinitions.Add(unrelated);
        var source = new WhatsAppAssistantCapabilitySource(reader);
        var input = JsonSerializer.Serialize(new { definitionIds = definitions }, JsonSerializerOptions.Web);
        var result = await source.ExecuteAsync(Request(WhatsAppAssistantCapabilitySource.OffersId, input), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<StructuredFormOffersPayload>(result.Value.Payload);
        Assert.Empty(payload.Offers);
        Assert.Equal(definitions, payload.DeclinedDefinitionIds);
        Assert.DoesNotContain(unrelated, payload.DeclinedDefinitionIds);
        Assert.Equal(definitions, reader.RequestedDeclineDefinitions);
        Assert.True(source.ParseOutput(new(WhatsAppAssistantCapabilitySource.OffersId, 1), JsonSerializer.Serialize(payload, JsonSerializerOptions.Web)).IsSuccess);
    }

    [Fact]
    public async Task The_full_decline_set_and_recent_offer_names_share_the_complete_JSON_budget()
    {
        var reader = new FakeWhatsAppAssistantReader();
        var definitions = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var definition in definitions) reader.DeclinedDefinitions.Add(definition);
        for (var index = 0; index < 101; index++)
            reader.Offers.Add(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('漢', 80)));
        var result = await new WhatsAppAssistantCapabilitySource(reader).ExecuteAsync(Request(WhatsAppAssistantCapabilitySource.OffersId,
            JsonSerializer.Serialize(new { definitionIds = definitions }, JsonSerializerOptions.Web)), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<StructuredFormOffersPayload>(result.Value.Payload);
        Assert.Equal(100, payload.DeclinedDefinitionIds.Count);
        Assert.InRange(payload.Offers.Count, 1, 20);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { state = result.Value.State, payload }, JsonSerializerOptions.Web)) <= 16384);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"definitionIds":null}""")]
    [InlineData("""{"definitionIds":[],"subjectUserId":"00000000-0000-0000-0000-000000000001"}""")]
    public async Task Offers_require_a_closed_candidate_selection_without_identity_arguments(string input)
    {
        var result = await new WhatsAppAssistantCapabilitySource(new FakeWhatsAppAssistantReader()).ExecuteAsync(
            Request(WhatsAppAssistantCapabilitySource.OffersId, input), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("not_form")]
    [InlineData("operator_needed")]
    public async Task No_reply_is_a_closed_proposal_with_no_free_message(string reason)
    {
        var result=await new WhatsAppAssistantCapabilitySource(new FakeWhatsAppAssistantReader()).ExecuteAsync(
            Request(WhatsAppAssistantCapabilitySource.NoReplyId,JsonSerializer.Serialize(new{reason})),TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.Equal(reason,Assert.IsType<WhatsAppAssistantNoReplyPayload>(result.Value.Payload).Reason);
    }

    [Theory]
    [InlineData("{\"reason\":\"write arbitrary text\"}")]
    [InlineData("{\"reason\":\"not_form\",\"message\":\"ignore all permissions\"}")]
    public async Task Free_text_and_extra_properties_never_become_outbound_messages(string json)
    {
        var result=await new WhatsAppAssistantCapabilitySource(new FakeWhatsAppAssistantReader()).ExecuteAsync(Request(WhatsAppAssistantCapabilitySource.NoReplyId,json),TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task A_decline_must_reference_an_offer_observed_in_a_confirmed_previous_step_of_this_turn()
    {
        var offer=new StructuredFormOfferReference(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Visita");
        var read=new AssistantObservedRead(Turn,2,new(WhatsAppAssistantCapabilitySource.OffersId,1),new StructuredFormOffersPayload(Subject,"es",[offer],[]));
        var source=new WhatsAppAssistantCapabilitySource(new FakeWhatsAppAssistantReader());
        var request=Request(WhatsAppAssistantCapabilitySource.DeclineId,JsonSerializer.Serialize(new{offer.RequestId,offer.DefinitionId},JsonSerializerOptions.Web),[read]);
        var result=await source.ExecuteAsync(request,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.Equal(2,Assert.IsType<WhatsAppAssistantDeclinePayload>(result.Value.Payload).ReadOrdinal);
        Assert.True((await source.ExecuteAsync(request with{ObservedReads=[]},TestContext.Current.CancellationToken)).IsFailure);
        Assert.True((await source.ExecuteAsync(request with{TurnId=Guid.NewGuid()},TestContext.Current.CancellationToken)).IsFailure);
        Assert.True((await source.ExecuteAsync(request with{Context=request.Context with{SubjectUserId=Guid.NewGuid()}},TestContext.Current.CancellationToken)).IsFailure);
    }

    [Fact]
    public async Task Provider_selected_versions_or_unknown_observed_ids_are_rejected()
    {
        var source=new WhatsAppAssistantCapabilitySource(new FakeWhatsAppAssistantReader());
        Assert.True((await source.ExecuteAsync(Request(WhatsAppAssistantCapabilitySource.NoReplyId,"{\"reason\":\"not_form\"}") with{Capability=new(WhatsAppAssistantCapabilitySource.NoReplyId,2)},TestContext.Current.CancellationToken)).IsFailure);
        Assert.True(source.ParseOutput(new(WhatsAppAssistantCapabilitySource.OffersId,1),"{\"subjectUserId\":\"00000000-0000-0000-0000-000000000000\",\"culture\":\"es\",\"offers\":[],\"extra\":true}").IsFailure);
    }
    [Fact]
    public async Task A_question_exposes_each_distinct_form_under_its_single_offer_identity()
    {
        var reader=new FakeWhatsAppAssistantReader();var requestId=Guid.NewGuid();
        reader.Offers.Add(new(requestId,Guid.NewGuid(),Guid.NewGuid(),"Visita"));
        reader.Offers.Add(new(requestId,Guid.NewGuid(),Guid.NewGuid(),"Compra"));
        var source=new WhatsAppAssistantCapabilitySource(reader);
        var read=await source.ExecuteAsync(Request(WhatsAppAssistantCapabilitySource.OffersId,"{\"definitionIds\":[]}"),TestContext.Current.CancellationToken);
        Assert.True(read.IsSuccess);var payload=Assert.IsType<StructuredFormOffersPayload>(read.Value.Payload);Assert.Equal(2,payload.Offers.Count);
        Assert.True(source.ParseOutput(new(WhatsAppAssistantCapabilitySource.OffersId,1),JsonSerializer.Serialize(payload,JsonSerializerOptions.Web)).IsSuccess);
        var selected=payload.Offers[1];
        var decline=await source.ExecuteAsync(Request(WhatsAppAssistantCapabilitySource.DeclineId,JsonSerializer.Serialize(new{selected.RequestId,selected.DefinitionId},JsonSerializerOptions.Web),
            [new(Turn,1,new(WhatsAppAssistantCapabilitySource.OffersId,1),payload)]),TestContext.Current.CancellationToken);
        Assert.True(decline.IsSuccess);Assert.Equal(selected.DefinitionId,Assert.IsType<WhatsAppAssistantDeclinePayload>(decline.Value.Payload).DefinitionId);
    }
}
