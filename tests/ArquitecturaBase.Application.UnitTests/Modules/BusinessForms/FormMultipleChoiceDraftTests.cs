using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormMultipleChoiceDraftTests
{
    private static readonly IReadOnlyDictionary<string,string> Text = new Dictionary<string,string> { ["es"]="Fallas", ["en"]="Faults" };
    private static SaveFormDraftRequest Request(FormSelectionSettings? selection, bool required = true, int count = 2) =>
        new(Guid.NewGuid(), Text, "MULTI", new(1,"es", Text,
            [new("faults",null,null,"multiChoice",Text,null,required,null,
                Enumerable.Range(1,count).Select(index=>new FormChoiceInput("option"+index,null,new Dictionary<string,string>{["es"]="OpciÃƒÆ’Ã‚Â³n "+index})).ToArray(),
                Selection: selection)]));

    [Theory]
    [InlineData(0,1,false)]
    [InlineData(1,1,true)]
    [InlineData(3,3,true)]
    [InlineData(0,20,false)]
    [InlineData(20,20,true)]
    public void Multiple_choice_accepts_the_decided_selection_bounds(int minimum,int maximum,bool required)
    {
        var request = Request(new(minimum,maximum),required,count:Math.Max(2,minimum));
        Assert.True(new SaveFormDraftRequestValidator().Validate(request).IsValid);
        var resolved = FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(request),null,FormRetiredIdentities.Empty);
        Assert.True(resolved.IsSuccess);
        var field = Assert.Single(resolved.Value.Draft.Fields);
        Assert.Equal("multiChoice",field.Type);
        Assert.Equal(new FormSelectionSettings(minimum,maximum),field.Selection);
        Assert.Equal(Math.Max(2,minimum),field.Options!.Count);
        Assert.Equal(Math.Max(2,minimum),resolved.Value.IdentityMap.Choices.Count);
    }

    [Theory]
    [InlineData(-1,1,false)]
    [InlineData(0,0,false)]
    [InlineData(0,21,false)]
    [InlineData(2,1,true)]
    [InlineData(0,3,true)]
    [InlineData(1,3,false)]
    public void Invalid_bounds_and_required_mismatches_fail_at_selection(int minimum,int maximum,bool required)
    {
        var errors = new SaveFormDraftRequestValidator().Validate(Request(new(minimum,maximum),required)).Errors;
        Assert.Contains(errors,error=>error.PropertyName=="draft.fields[0].selection");
    }

    [Fact]
    public void The_minimum_cannot_exceed_the_number_of_own_options()
    {
        var result = new SaveFormDraftRequestValidator().Validate(Request(new(3,3),count:2));
        Assert.Contains(result.Errors,error=>error.PropertyName=="draft.fields[0].selection");
    }

    [Fact]
    public void Only_multiple_choice_may_have_selection_and_it_must_have_it()
    {
        var validator = new SaveFormDraftRequestValidator();
        Assert.Contains(validator.Validate(Request(null)).Errors,error=>error.PropertyName=="draft.fields[0].selection");
        var request = Request(new(1,2));
        foreach(var type in new[]{"choice","shortText","photo","document"})
        {
            var field = request.Draft.Fields[0] with { Type = type };
            Assert.Contains(validator.Validate(request with { Draft=request.Draft with { Fields=[field] } }).Errors,
                error=>error.PropertyName=="draft.fields[0].selection");
        }
    }

    [Theory]
    [InlineData(0,false)]
    [InlineData(1,true)]
    [InlineData(20,true)]
    [InlineData(21,false)]
    public void Own_options_are_limited_to_twenty(int count,bool accepted) =>
        Assert.Equal(accepted,new SaveFormDraftRequestValidator().Validate(Request(new(1,2),count:count)).IsValid);

    [Fact]
    public void A_catalog_multiple_choice_has_no_embedded_options()
    {
        var request = Request(new(1,3));
        var field = request.Draft.Fields[0] with { Options=null, Source=new(Guid.NewGuid()) };
        request=request with { Draft=request.Draft with { Fields=[field] } };
        Assert.True(new SaveFormDraftRequestValidator().Validate(request).IsValid);
        var result=FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(request),null,FormRetiredIdentities.Empty);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Draft.Fields[0].Options);
        Assert.Equal(field.Source,result.Value.Draft.Fields[0].Source);
    }

    [Fact]
    public void Changing_choice_to_multiple_creates_a_new_identity_and_retires_the_original()
    {
        var original = Request(null) with { Draft=Request(null).Draft with { Fields=[Request(null).Draft.Fields[0] with { Type="choice" }] } };
        var first=FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(original),null,FormRetiredIdentities.Empty).Value;
        var definition=BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1,"Fallas",FormDraftPolicy.Serialize(Text),"MULTI","es",FormDraftPolicy.Serialize(Text),
            FormDraftPolicy.Serialize(new FormQuestionSchema(1,first.Draft.Fields)),1,true).IsSuccess);
        var request=Request(new(1,2));
        var old=first.Draft.Fields[0];
        var reused=request with { Draft=request.Draft with { Fields=[request.Draft.Fields[0] with { FieldId=old.FieldId }] } };
        Assert.True(FormDraftPolicy.Resolve(reused,definition,FormRetiredIdentities.Empty).IsFailure);
        var replacement=request with { Draft=request.Draft with { Fields=[request.Draft.Fields[0] with { ReplacesFieldId=old.FieldId }] } };
        var result=FormDraftPolicy.Resolve(replacement,definition,FormRetiredIdentities.Empty);
        Assert.True(result.IsSuccess);
        Assert.NotEqual(old.FieldId,result.Value.Draft.Fields[0].FieldId);
        Assert.Contains(old.FieldId,result.Value.NewlyRetiredIdentities.Fields);
        Assert.All(old.Options!,option=>Assert.Contains(option.ChoiceId,result.Value.NewlyRetiredIdentities.Choices));
    }

    [Fact]
    public void Publication_snapshot_preserves_selection_and_detects_a_change_in_it()
    {
        var input=FormDraftPolicy.Normalize(Request(new(1,2)));
        var resolution=FormDraftPolicy.Resolve(input,null,FormRetiredIdentities.Empty).Value;
        var definition=new FormDefinitionResponse(Guid.NewGuid(),input.Name,"MULTI",false,1,resolution.Draft);
        var question=Assert.Single(FormPublicationSnapshotPolicy.ChannelRequest(definition).Questions);
        Assert.Equal(new StructuredFormSelectionSettings(1,2),question.Selection);
        var field=resolution.Draft.Fields[0];
        var savedInput=input.Draft.Fields[0] with { FieldId=field.FieldId,
            Options=field.Options!.Select((option,index)=>new FormChoiceInput("option"+index,option.ChoiceId,option.Label)).ToArray() };
        input=input with { Draft=input.Draft with { Fields=[savedInput] } };
        Assert.True(FormPublicationSnapshotPolicy.Matches(input,definition));
        Assert.False(FormPublicationSnapshotPolicy.Matches(input with { Draft=input.Draft with { Fields=[savedInput with { Selection=new(1,3) }] } },definition));
    }

    [Fact]
    public void Adding_selection_preserves_old_schema_bytes_by_omitting_nulls()
    {
        var field=new CanonicalFormField(Guid.NewGuid(),"field_a","choice",Text,null,false,null,[]);
        var json=FormDraftPolicy.Serialize(field);
        Assert.DoesNotContain("selection",json,StringComparison.Ordinal);
        var legacy=FormDraftPolicy.Read<CanonicalFormField>(json);
        Assert.Null(legacy.Selection);
        Assert.Equal(json,FormDraftPolicy.Serialize(legacy));
    }
}
