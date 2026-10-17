using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
internal sealed class FormRecordCaptureOriginFixture
{
    public BusinessFormCaptureFixture Capture {get;}
    public FormVersionSchema Schema {get;}
    public CanonicalFormField Field=>Schema.Questions[0];
    public FakeFormResponseReader Responses => Capture.OriginResponses;
    public FormSuggestionFixture Suggestions => Capture.Suggestions;
    public FormRecordCaptureOriginPolicy Policy {get;}
    public Guid OperatorId {get;}=Guid.NewGuid();
    public Guid SourceFieldId {get;}=Guid.NewGuid();
    public StructuredRecordCaptureRequest Request {get;set;}
    public FormRecordCaptureOriginFixture(string type="shortText",string proposed="local",string? final=null)
    {
        Capture=new(type,false);
        Schema=FormDraftPolicy.Read<FormVersionSchema>(Capture.Version.SchemaJson);
        Request=Capture.Request(new Dictionary<string,string>{{Capture.Key,final??proposed}});
        Policy=new(Responses,Suggestions);
    }
    public StructuredRecordCaptureOrigin Assistant(string? value=null)=>new(StructuredRecordSenderKind.Assistant,null,"Reconoció una visita",
        new(Guid.NewGuid(),"forms.propose-registration",1,new AssistantOperationProvenance("test-profile",1,"test-instructions",1,new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc),"whatsapp-assistant")),
        [new(Field.FieldId,Field.Type,value??Request.Answers[Field.Key])],[Field.FieldId]);
    public StructuredRecordCaptureOrigin Manual()=>new(StructuredRecordSenderKind.Manual,OperatorId,null,null,[],[]);
    public StructuredRecordCaptureOrigin Lineage(string mode="Always",string sourceValue="local")
    {
        var sourceId=Guid.NewGuid();var sourceVersion=Guid.NewGuid();
        var sourceField=Field with{FieldId=SourceFieldId,Key="field_"+SourceFieldId.ToString("N")};
        var source=new FormRecordStoredRow(Guid.NewGuid(),sourceId,12,Suggestions.Definition.Id,"ENT",sourceVersion,1,"{}",
            FormDraftPolicy.Serialize(new FormVersionSchema(1,[sourceField])),FormDraftPolicy.Serialize(new[]{new{FieldId=SourceFieldId,Value=sourceValue}}));
        Responses.ExportRows=[new(source,new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc),new(Capture.Actor,"Author"))];
        var suggestion=new FormSuggestion(Guid.NewGuid(),Capture.Definition.Id,mode,mode=="Assistant"?"Cuando lo pida":null,true,[],[new(new("Field",SourceFieldId),Field.FieldId)]){DestinationVersionId=Capture.VersionId};
        var set=BusinessFormSuggestionSet.Create(source.DefinitionId,source.VersionId,1,"es",FormDraftPolicy.Serialize(new[]{suggestion}));Suggestions.Sets.Add(set);
        var origin=Assistant(sourceValue);
        return origin with{Proposal=mode=="Always"?null:origin.Proposal,Suggestion=new(sourceId,suggestion.Id,set.Id,[new("Field",SourceFieldId,Field.FieldId)])};
    }
}
