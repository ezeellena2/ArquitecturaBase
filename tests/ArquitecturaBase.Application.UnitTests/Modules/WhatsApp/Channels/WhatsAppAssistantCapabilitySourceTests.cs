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
        reader.Offers.Add(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Visita ficticia",false));
        var source=new WhatsAppAssistantCapabilitySource(reader);
        var result=await source.ExecuteAsync(Request(WhatsAppAssistantCapabilitySource.OffersId,"{}"),TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var payload=Assert.IsType<StructuredFormOffersPayload>(result.Value.Payload);
        Assert.Equal(Subject,payload.SubjectUserId); Assert.Single(payload.Offers);
        var decoded=source.ParseOutput(new(WhatsAppAssistantCapabilitySource.OffersId,1),JsonSerializer.Serialize(payload,JsonSerializerOptions.Web));
        Assert.True(decoded.IsSuccess); Assert.IsType<StructuredFormOffersPayload>(decoded.Value);
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
        var offer=new StructuredFormOfferReference(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Visita",false);
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
}
