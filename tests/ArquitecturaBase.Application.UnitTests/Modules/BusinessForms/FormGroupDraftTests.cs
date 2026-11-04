using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// The block of rows (RG1-A, RG6-A): a "group" question followed, without gaps, by its questions marked "repeated".
public sealed class FormGroupDraftTests
{
    private static readonly IReadOnlyDictionary<string,string> Text = new Dictionary<string,string> { ["es"]="Artículos" };

    private static FormFieldInput Group(FormRowSettings? rows, bool required = true, string localId = "rows") =>
        new(localId,null,null,"group",Text,null,required,null,null,Rows: rows);

    private static FormFieldInput Group() => Group(new(1,20));

    private static FormFieldInput Question(string localId, string type = "shortText", bool? repeated = true, Guid? fieldId = null, Guid? replaces = null) =>
        new(localId,fieldId,replaces,type,new Dictionary<string,string>{ ["es"]=localId },null,false,null,
            type is "choice" ? [new FormChoiceInput(localId+"-a",null,new Dictionary<string,string>{ ["es"]="A" })] : null,
            Repeated: repeated);

    private static SaveFormDraftRequest Request(params FormFieldInput[] fields) => new(Guid.NewGuid(),Text,"INS",new(1,"es",Text,fields));

    private static string[] Errors(SaveFormDraftRequest request) =>
        new SaveFormDraftRequestValidator().Validate(request).Errors.Select(error => error.PropertyName).ToArray();

    private static FormDraftResolution Resolve(SaveFormDraftRequest request, BusinessFormDefinition? existing = null)
    {
        var result = FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(request),existing,FormRetiredIdentities.Empty);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static BusinessFormDefinition Saved(FormDraftResolution resolution)
    {
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1,"Recepción",FormDraftPolicy.Serialize(Text),"INS","es",FormDraftPolicy.Serialize(Text),
            FormDraftPolicy.Serialize(new FormQuestionSchema(1,resolution.Draft.Fields)),1,true).IsSuccess);
        return definition;
    }

    [Fact]
    public void A_block_and_its_repeated_questions_are_saved_with_their_rows()
    {
        var request = Request(Question("proveedor",repeated:null),Group(),Question("articulo","choice"),Question("cantidad","decimal"),
            Question("observaciones","longText"),Question("remito",repeated:null));
        Assert.Empty(Errors(request));

        var fields = Resolve(request).Draft.Fields;

        Assert.Equal(new[] { "shortText","group","choice","decimal","longText","shortText" },fields.Select(field => field.Type));
        Assert.Equal(new FormRowSettings(1,20),fields[1].Rows);
        Assert.Null(fields[1].Repeated);
        Assert.Null(fields[1].Options);
        Assert.Null(fields[1].MaxLength);
        Assert.Equal(new bool?[] { null,null,true,true,true,null },fields.Select(field => field.Repeated));
        Assert.All(fields.Where(field => field.Type != "group"),field => Assert.Null(field.Rows));
    }

    [Theory]
    [InlineData(0,1,false)]
    [InlineData(1,1,true)]
    [InlineData(1,20,true)]
    [InlineData(0,100,false)]
    [InlineData(100,100,true)]
    public void The_block_accepts_the_decided_row_bounds(int minimum, int maximum, bool required)
    {
        var request = Request(Group(new(minimum,maximum),required),Question("cantidad","decimal"));
        Assert.Empty(Errors(request));
        Assert.Equal(new FormRowSettings(minimum,maximum),Resolve(request).Draft.Fields[0].Rows);
    }

    [Theory]
    [InlineData(-1,1,false)]
    [InlineData(0,0,false)]
    [InlineData(0,101,false)]
    [InlineData(101,101,true)]
    [InlineData(3,2,true)]
    [InlineData(0,5,true)]
    [InlineData(1,5,false)]
    public void Invalid_row_bounds_and_required_mismatches_fail_at_rows(int minimum, int maximum, bool required) =>
        Assert.Contains("draft.fields[0].rows",Errors(Request(Group(new(minimum,maximum),required),Question("cantidad","decimal"))));

    [Fact]
    public void Only_a_block_has_rows_and_it_must_have_them()
    {
        Assert.Contains("draft.fields[0].rows",Errors(Request(Group(null))));
        Assert.Contains("draft.fields[0].rows",Errors(Request(Question("cantidad","decimal",repeated:null) with { Rows=new(1,20) })));
    }

    [Fact]
    public void A_block_carries_no_options_length_catalog_files_selection_or_repetition()
    {
        var group = Group();
        Assert.Contains("draft.fields[0].options",Errors(Request(group with { Options=[new("a",null,Text)] })));
        Assert.Contains("draft.fields[0].maxLength",Errors(Request(group with { MaxLength=80 })));
        Assert.Contains("draft.fields[0].source.catalog",Errors(Request(group with { Source=new(Guid.NewGuid()) })));
        Assert.Contains("draft.fields[0].files",Errors(Request(group with { Files=new(0,1) })));
        Assert.Contains("draft.fields[0].selection",Errors(Request(group with { Selection=new(0,1) })));
        Assert.Contains("draft.fields[0].repeated",Errors(Request(group with { Repeated=true })));
    }

    [Fact]
    public void A_form_has_at_most_one_block()
    {
        var errors = Errors(Request(Group(),Question("cantidad","decimal"),Group(new(1,20),localId:"other")));
        Assert.Contains("draft.fields[2].type",errors);
        Assert.DoesNotContain("draft.fields[0].type",errors);
    }

    [Fact]
    public void Repeated_questions_follow_the_block_without_gaps()
    {
        Assert.Contains("draft.fields[0].repeated",Errors(Request(Question("cantidad","decimal"))));
        Assert.Contains("draft.fields[0].repeated",Errors(Request(Question("cantidad","decimal"),Group())));
        var gap = Errors(Request(Group(),Question("articulo"),Question("remito",repeated:null),Question("cantidad","decimal")));
        Assert.Equal(new[] { "draft.fields[3].repeated" },gap);
        Assert.Empty(Errors(Request(Question("proveedor",repeated:null),Group(),Question("articulo"),Question("remito",repeated:null))));
    }

    [Theory]
    [InlineData("shortText",true)]
    [InlineData("longText",true)]
    [InlineData("decimal",true)]
    [InlineData("date",true)]
    [InlineData("boolean",true)]
    [InlineData("choice",true)]
    [InlineData("multiChoice",false)]
    [InlineData("photo",false)]
    [InlineData("document",false)]
    public void Only_the_types_that_fit_a_row_can_be_repeated(string type, bool accepted) =>
        Assert.Equal(!accepted,Errors(Request(Group(),Question("pregunta",type))).Contains("draft.fields[1].repeated"));

    [Fact]
    public void A_catalog_choice_can_be_repeated()
    {
        var request = Request(Group(),Question("articulo","choice") with { Options=null, Source=new(Guid.NewGuid()) });
        Assert.Empty(Errors(request));
        Assert.True(Resolve(request).Draft.Fields[1].Repeated);
    }

    [Fact]
    public void An_empty_block_can_be_saved() =>
        Assert.Equal("group",Assert.Single(Resolve(Request(Group())).Draft.Fields).Type);

    [Fact]
    public void The_block_does_not_count_toward_the_forty_questions()
    {
        var forty = Enumerable.Range(1,40).Select(index => Question("q"+index,repeated:null)).ToArray();
        Assert.Empty(Errors(Request([..forty,Group()])));
        Assert.Contains("draft.fields",Errors(Request([..forty,Question("q41",repeated:null),Group()])));
        Assert.Equal(40,FormDraftPolicy.QuestionCount(Resolve(Request([..forty,Group()])).Draft.Fields));
    }

    [Fact]
    public void Moving_a_saved_question_into_the_block_creates_a_new_identity_and_retires_the_old_one()
    {
        var first = Resolve(Request(Question("cantidad","decimal",repeated:null)));
        var old = first.Draft.Fields[0];

        var moved = Resolve(Request(Group(),Question("cantidad-fila","decimal",replaces:old.FieldId)),Saved(first));

        var child = moved.Draft.Fields[1];
        Assert.True(child.Repeated);
        Assert.NotEqual(old.FieldId,child.FieldId);
        Assert.Contains(old.FieldId,moved.NewlyRetiredIdentities.Fields);
        Assert.Equal(old.FieldId,moved.IdentityMap.Fields[1].ReplacesFieldId);
    }

    [Fact]
    public void Moving_a_question_out_of_the_block_creates_a_new_identity()
    {
        var first = Resolve(Request(Group(),Question("cantidad","decimal")));
        var old = first.Draft.Fields[1];

        var moved = Resolve(Request(Group(),Question("cantidad-suelta","decimal",repeated:null,replaces:old.FieldId)),Saved(first));

        Assert.Null(moved.Draft.Fields[1].Repeated);
        Assert.NotEqual(old.FieldId,moved.Draft.Fields[1].FieldId);
        Assert.Contains(old.FieldId,moved.NewlyRetiredIdentities.Fields);
    }

    [Fact]
    public void Keeping_an_identity_while_changing_its_repetition_is_rejected()
    {
        var first = Resolve(Request(Question("cantidad","decimal",repeated:null)));
        var request = FormDraftPolicy.Normalize(Request(Group(),Question("cantidad","decimal",fieldId:first.Draft.Fields[0].FieldId)));

        var result = FormDraftPolicy.Resolve(request,Saved(first),FormRetiredIdentities.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(FormDraftErrors.QuestionConfigurationInvalid.Code,result.Error.Code);
        Assert.Contains("draft.fields[1].repeated",Assert.IsType<ValidationError>(result.Error).Errors.Keys);
    }

    [Fact]
    public void A_replacement_must_change_the_type_the_catalog_or_the_repetition()
    {
        var first = Resolve(Request(Group(),Question("cantidad","decimal")));
        var request = FormDraftPolicy.Normalize(Request(Group(),Question("cantidad-otra","decimal",replaces:first.Draft.Fields[1].FieldId)));

        var result = FormDraftPolicy.Resolve(request,Saved(first),FormRetiredIdentities.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(FormDraftErrors.QuestionConfigurationInvalid.Code,result.Error.Code);
    }

    [Fact]
    public void Changing_the_rows_keeps_the_block_identity()
    {
        var first = Resolve(Request(Group(),Question("cantidad","decimal")));
        var block = first.Draft.Fields[0];

        var changed = Resolve(Request(Group(new(0,50),required:false) with { FieldId=block.FieldId, LocalId=block.FieldId.ToString() },
            Question("cantidad","decimal",fieldId:first.Draft.Fields[1].FieldId)),Saved(first));

        Assert.Equal(block.FieldId,changed.Draft.Fields[0].FieldId);
        Assert.Equal(new FormRowSettings(0,50),changed.Draft.Fields[0].Rows);
        Assert.Empty(changed.NewlyRetiredIdentities.Fields);
    }

    [Fact]
    public void Repeated_false_is_normalized_away_and_old_schema_bytes_are_kept()
    {
        var normalized = FormDraftPolicy.Normalize(Request(Question("cantidad","decimal",repeated:false)));
        Assert.Null(normalized.Draft.Fields[0].Repeated);

        var field = new CanonicalFormField(Guid.NewGuid(),"field_a","decimal",Text,null,false,null,null);
        var json = FormDraftPolicy.Serialize(field);
        Assert.DoesNotContain("rows",json,StringComparison.Ordinal);
        Assert.DoesNotContain("repeated",json,StringComparison.Ordinal);
        Assert.Equal(json,FormDraftPolicy.Serialize(FormDraftPolicy.Read<CanonicalFormField>(json)));
    }
}
