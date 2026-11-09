using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// CO1-A: a question outside the block shows only when an earlier question outside the block (choose one, own or from a catalog,
// or yes/no) has one of the answers given. The draft seals the question and its new options by their identities; publishing
// checks the rest.
public sealed class FormConditionDraftTests
{
    private static readonly IReadOnlyDictionary<string,string> Text = new Dictionary<string,string> { ["es"]="Factura" };
    private const string Kind = "0196f2a0-0000-7000-8000-0000000000c1";
    private const string From = "0196f2a0-0000-7000-8000-0000000000c2";

    private static FormFieldInput KindQuestion() => new(Kind,null,null,"choice",new Dictionary<string,string>{ ["es"]="Concepto" },null,true,null,
        [new("goods",null,new Dictionary<string,string>{ ["es"]="Productos" }), new("services",null,new Dictionary<string,string>{ ["es"]="Servicios" }),
         new("both",null,new Dictionary<string,string>{ ["es"]="Productos y servicios" })]);

    private static FormFieldInput Period(FormShowWhen? showWhen, string type = "date", bool? repeated = null) =>
        new(From,null,null,type,new Dictionary<string,string>{ ["es"]="Período desde" },null,true,null,null,Repeated: repeated,ShowWhen: showWhen);

    private static SaveFormDraftRequest Request(params FormFieldInput[] fields) => new(Guid.NewGuid(),Text,"FAC",new(1,"es",Text,fields));

    private static string[] Errors(SaveFormDraftRequest request) =>
        new SaveFormDraftRequestValidator().Validate(request).Errors.Select(error => error.PropertyName).ToArray();

    [Fact]
    public void The_question_and_its_new_options_are_sealed_by_their_identities()
    {
        var request = Request(KindQuestion(),Period(new(Guid.Parse(Kind),["services","both"])));
        Assert.Empty(Errors(request));

        var result = FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(request),null,FormRetiredIdentities.Empty);

        Assert.True(result.IsSuccess);
        var fields = result.Value.Draft.Fields;
        Assert.Equal(fields[0].FieldId,fields[1].ShowWhen!.QuestionId);
        Assert.Equal(new[] { fields[0].Options![1].ChoiceId.ToString("D"),fields[0].Options![2].ChoiceId.ToString("D") },fields[1].ShowWhen!.Values);
        Assert.DoesNotContain("showWhen",FormDraftPolicy.Serialize(fields[0]));
    }

    [Fact]
    public void A_condition_names_another_question_with_some_answers()
    {
        Assert.Contains("draft.fields[1].showWhen",Errors(Request(KindQuestion(),Period(new(Guid.Parse(From),["x"])))));
        Assert.Contains("draft.fields[1].showWhen",Errors(Request(KindQuestion(),Period(new(Guid.Empty,["x"])))));
        Assert.Contains("draft.fields[1].showWhen",Errors(Request(KindQuestion(),Period(new(Guid.Parse(Kind),[])))));
        Assert.Contains("draft.fields[1].showWhen",Errors(Request(KindQuestion(),Period(new(Guid.Parse(Kind),["a","a"])))));
    }

    [Fact]
    public void Neither_the_block_nor_its_questions_take_a_condition()
    {
        var block = new FormFieldInput("items",null,null,"group",Text,null,true,null,null,Rows: new(1,5));
        Assert.Contains("draft.fields[2].showWhen",Errors(Request(KindQuestion(),block,Period(new(Guid.Parse(Kind),["services"]),"decimal",true))));
    }

    private static CanonicalFormField Field(Guid id, string type, FormShowWhen? showWhen = null, bool? repeated = null, IReadOnlyList<CanonicalFormChoice>? options = null) =>
        new(id,"field_x",type,new Dictionary<string,string>{ ["es"]="Dato" },null,false,null,options,Repeated: repeated,ShowWhen: showWhen);

    private static FormDefinitionResponse Definition(params CanonicalFormField[] fields) => new(Guid.NewGuid(),Text,"FAC",false,1,new FormDraftResponse(1,"es",Text,fields));

    [Fact]
    public void Publishing_checks_the_question_goes_before_and_the_answers_exist()
    {
        var services = new CanonicalFormChoice(Guid.CreateVersion7(),new Dictionary<string,string>{ ["es"]="Servicios" });
        var kind = Field(Guid.CreateVersion7(),"choice",options: [services]);
        var paid = Field(Guid.CreateVersion7(),"boolean");
        Assert.Empty(FormPublicationPolicy.ValidateConditions(Definition(kind,paid,
            Field(Guid.CreateVersion7(),"date",new(kind.FieldId,[services.ChoiceId.ToString("D")])),Field(Guid.CreateVersion7(),"date",new(paid.FieldId,["true"])))));

        var later = Field(Guid.CreateVersion7(),"date",new(kind.FieldId,[services.ChoiceId.ToString("D")]));
        var issue = Assert.Single(FormPublicationPolicy.ValidateConditions(Definition(later,kind)));
        Assert.Equal(FormPublicationErrors.ConditionInvalid.Code,issue.Code);
        Assert.Equal("draft.fields[0].showWhen",issue.Path);
        Assert.Single(FormPublicationPolicy.ValidateConditions(Definition(kind,Field(Guid.CreateVersion7(),"date",new(kind.FieldId,[Guid.CreateVersion7().ToString("D")])))));
        Assert.Single(FormPublicationPolicy.ValidateConditions(Definition(paid,Field(Guid.CreateVersion7(),"date",new(paid.FieldId,["maybe"])))));
        var text = Field(Guid.CreateVersion7(),"shortText");
        Assert.Single(FormPublicationPolicy.ValidateConditions(Definition(text,Field(Guid.CreateVersion7(),"date",new(text.FieldId,["x"])))));
    }
}
