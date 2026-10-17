using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppAssistantFlowPayloadTests
{
    private static readonly WhatsAppOptions Settings=new(){WabaId="1",PhoneNumberId="2",GraphApiVersion="v25.0"};
    private static JsonObject Navigation(IReadOnlyList<StructuredFormCatalogOffer>? catalogs=null,
        IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>>? initial=null)
    {
        var message=new WhatsAppFlowMessageResponse(PhoneNumber.Create("+5493413654800").Value,"Completá el formulario.","Abrir","123","private-token","FORM",
            new("1","2","v25.0","configuration"),catalogs,initial);
        return WhatsAppMessagePayload.Build(message,Settings)["interactive"]!["action"]!["parameters"]!["flow_action_payload"]!.AsObject();
    }
    [Fact]
    public void Scalar_initial_values_reach_the_published_form_as_wire_strings_without_inventing_missing_fields()
    {
        var navigation=Navigation(initial:new Dictionary<string,IReadOnlyDictionary<string,string>>{
            ["prefill_FORM"]=new Dictionary<string,string>{{"field_boolean","false"},{"field_decimal","0"}}});
        Assert.True(navigation.ContainsKey("data"));var data=navigation["data"]!.AsObject();
        Assert.True(data.ContainsKey("prefill_FORM"));var fields=data["prefill_FORM"]!.AsObject();
        Assert.Equal("false",fields["field_boolean"]!.GetValue<string>());Assert.Equal("0",fields["field_decimal"]!.GetValue<string>());
        Assert.Equal(2,fields.Count);
    }
    [Fact]
    public void Initial_values_and_the_frozen_catalog_offer_share_the_same_navigation_data()
    {
        var field=Guid.NewGuid();var option=Guid.NewGuid();var key="field_"+field.ToString("N");
        var navigation=Navigation([new(field,key,"Persona",Guid.NewGuid(),1,"Manual","Personas",[new(option,"Persona ficticia",null)])],
            new Dictionary<string,IReadOnlyDictionary<string,string>>{{"prefill_FORM",new Dictionary<string,string>{{key,option.ToString("D")}}}});
        var data=navigation["data"]!.AsObject();Assert.True(data.ContainsKey(key+"_options"));Assert.True(data.ContainsKey("prefill_FORM"));
        Assert.Equal(option.ToString("D"),data["prefill_FORM"]![key]!.GetValue<string>());
    }
    [Fact]
    public void A_legacy_session_without_initial_values_or_catalogs_keeps_its_existing_navigation_shape()
    {
        var navigation=Navigation();Assert.Equal("FORM",navigation["screen"]!.GetValue<string>());Assert.False(navigation.ContainsKey("data"));
    }
}
