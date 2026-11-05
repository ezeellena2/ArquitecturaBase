using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Section titles (CW15-B): an element of the draft that is not a question. Each one says how many questions go before it.
public sealed class FormSectionDraftTests
{
    private static readonly IReadOnlyDictionary<string,string> Text = new Dictionary<string,string> { ["es"]="Aplicación" };

    private static Dictionary<string,string> Title(string es, string? en = null) =>
        en is null ? new Dictionary<string,string> { ["es"]=es } : new Dictionary<string,string> { ["es"]=es, ["en"]=en };

    private static FormFieldInput Question(string localId, string type = "shortText", bool? repeated = null) =>
        new(localId,null,null,type,new Dictionary<string,string>{ ["es"]=localId },null,false,null,null,Repeated: repeated);

    private static FormFieldInput Group() => new("rows",null,null,"group",Text,null,true,null,null,Rows: new(1,20));

    private static SaveFormDraftRequest Request(IReadOnlyList<FormSection>? sections, params FormFieldInput[] fields) =>
        new(Guid.NewGuid(),Text,"APL",new(1,"es",Text,fields,sections));

    private static string[] Errors(SaveFormDraftRequest request) =>
        new SaveFormDraftRequestValidator().Validate(request).Errors.Select(error => error.PropertyName).ToArray();

    private static FormDraftResolution Resolve(SaveFormDraftRequest request)
    {
        var result = FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(request),null,FormRetiredIdentities.Empty);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static readonly FormFieldInput[] Four = [Question("lote"),Question("hectareas","decimal"),Question("producto"),Question("dosis","decimal")];

    [Fact]
    public void Titles_are_saved_apart_from_the_questions_with_where_they_start()
    {
        var request = Request([new(0,Title("Lote")),new(2,Title("Productos","Products"))],Four);
        Assert.Empty(Errors(request));

        var draft = Resolve(request).Draft;

        Assert.Equal(4,draft.Fields.Count);
        Assert.Equal(new[] { 0,2 },draft.Sections!.Select(section => section.Start));
        Assert.Equal("Productos",draft.Sections![1].Title["es"]);
        Assert.Equal("Products",draft.Sections![1].Title["en"]);
        var schema = FormDraftPolicy.Serialize(new FormQuestionSchema(1,draft.Fields,draft.Sections));
        Assert.Contains("\"sections\":[{\"start\":0,\"title\":{\"es\":\"Lote\"}}",schema,StringComparison.Ordinal);
    }

    [Fact]
    public void A_draft_without_titles_keeps_its_json_and_no_titles_is_none()
    {
        var withoutList = Resolve(Request(null,Four)).Draft;
        var withEmptyList = Resolve(Request([],Four)).Draft;

        Assert.Null(withoutList.Sections);
        Assert.Null(withEmptyList.Sections);
        Assert.DoesNotContain("sections",FormDraftPolicy.Serialize(new FormQuestionSchema(1,withoutList.Fields,withoutList.Sections)),StringComparison.Ordinal);
        Assert.DoesNotContain("sections",FormDraftPolicy.Serialize(withEmptyList),StringComparison.Ordinal);
    }

    [Fact]
    public void A_saved_draft_reads_its_titles_back()
    {
        var draft = Resolve(Request([new(1,Title("Productos"))],Four)).Draft;
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1,"Aplicación",FormDraftPolicy.Serialize(Text),"APL","es",FormDraftPolicy.Serialize(Text),
            FormDraftPolicy.Serialize(new FormQuestionSchema(1,draft.Fields,draft.Sections)),4,true).IsSuccess);

        var read = FormDraftPolicy.Definition(definition).Draft;

        var section = Assert.Single(read.Sections!);
        Assert.Equal(1,section.Start);
        Assert.Equal("Productos",section.Title["es"]);
    }

    [Fact]
    public void A_title_needs_its_original_text_up_to_eighty_characters_and_the_translation_is_optional()
    {
        Assert.Contains("draft.sections[0].title.es",Errors(Request([new(0,Title(" "))],Four)));
        Assert.Contains("draft.sections[0].title.es",Errors(Request([new(0,new Dictionary<string,string> { ["en"]="Products" })],Four)));
        Assert.Contains("draft.sections[0].title.es",Errors(Request([new(0,Title(new string('a',81)))],Four)));
        Assert.Contains("draft.sections[0].title.fr",Errors(Request([new(0,new Dictionary<string,string> { ["es"]="Lote", ["fr"]="Lot" })],Four)));
        Assert.Empty(Errors(Request([new(0,Title(new string('a',80)))],Four)));
        Assert.Empty(Errors(Request([new(0,Title("Lote",""))],Four)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void A_title_starts_among_the_questions(int start) =>
        Assert.Contains("draft.sections[0].start",Errors(Request([new(start,Title("Lote"))],Four)));

    [Fact]
    public void Titles_go_in_the_order_of_the_questions()
    {
        Assert.Contains("draft.sections[1].start",Errors(Request([new(2,Title("Productos")),new(1,Title("Lote"))],Four)));
        Assert.Contains("draft.sections[1]",Errors(Request([new(0,Title("Lote")),null!],Four)));
    }

    [Fact]
    public void A_title_at_the_end_or_right_after_another_is_saved_and_publishing_decides()
    {
        var request = Request([new(0,Title("Lote")),new(0,Title("Otra")),new(4,Title("Al final"))],Four);

        Assert.Empty(Errors(request));
        Assert.Equal(new[] { 0,0,4 },Resolve(request).Draft.Sections!.Select(section => section.Start));
    }

    [Fact]
    public void A_title_goes_before_or_after_the_block_of_rows_but_never_inside_it()
    {
        FormFieldInput[] fields = [Question("proveedor"),Group(),Question("articulo",repeated: true),Question("cantidad","decimal",repeated: true),Question("remito")];

        Assert.Empty(Errors(Request([new(1,Title("Artículos")),new(4,Title("Remito"))],fields)));
        Assert.Contains("draft.sections[0].start",Errors(Request([new(2,Title("Adentro"))],fields)));
        Assert.Contains("draft.sections[0].start",Errors(Request([new(3,Title("Adentro"))],fields)));
    }

    [Fact]
    public void A_form_has_at_most_forty_titles() =>
        Assert.Contains("draft.sections",Errors(Request(Enumerable.Range(0,41).Select(_ => new FormSection(0,Title("Lote"))).ToArray(),Four)));

    [Fact]
    public void Checking_publication_requires_the_saved_titles()
    {
        var request = FormDraftPolicy.Normalize(Request([new(2,Title("Productos"))],Four));
        var resolution = FormDraftPolicy.Resolve(request,null,FormRetiredIdentities.Empty).Value;
        var saved = new FormDefinitionResponse(Guid.NewGuid(),request.Name,"APL",false,1,resolution.Draft);
        var input = request with { Draft = request.Draft with { Fields = request.Draft.Fields
            .Select((field,index) => field with { FieldId = resolution.Draft.Fields[index].FieldId }).ToArray() } };

        Assert.True(FormPublicationSnapshotPolicy.Matches(input,saved));
        Assert.False(FormPublicationSnapshotPolicy.Matches(input with { Draft = input.Draft with { Sections = [new(1,Title("Productos"))] } },saved));
        Assert.False(FormPublicationSnapshotPolicy.Matches(input with { Draft = input.Draft with { Sections = null } },saved));
    }
}
