using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Publishing the block of rows: the empty block blocks, a maximum over the channel rows warns (RG3-A) and the questions that
// entered the block read as «Pasan a renglones» (RG6-A).
public sealed class FormGroupPublicationTests
{
    private static readonly IReadOnlyDictionary<string,string> Text = new Dictionary<string,string> { ["es"]="Artículos" };

    private static CanonicalFormField Field(string type, string label = "Cantidad", bool? repeated = null, FormRowSettings? rows = null, Guid? catalog = null) =>
        new(Guid.CreateVersion7(),"field_x",type,new Dictionary<string,string>{ ["es"]=label },null,rows is { MinRows: > 0 },null,
            type == "choice" && catalog is null ? [] : null,catalog is null ? null : new FormQuestionSource(catalog),Rows: rows,Repeated: repeated);

    private static FormDefinitionResponse Definition(params CanonicalFormField[] fields) =>
        new(Guid.NewGuid(),Text,"INS",false,1,new FormDraftResponse(1,"es",Text,fields));

    [Fact]
    public void An_empty_block_blocks_publication_at_the_block()
    {
        var block = Field("group",rows: new(1,20));

        var issue = Assert.Single(FormPublicationPolicy.ValidateRows(Definition(Field("shortText"),block,Field("date"))));

        Assert.Equal(FormPublicationErrors.GroupEmpty.Code,issue.Code);
        Assert.Equal(block.FieldId,issue.FieldId);
        Assert.Equal("draft.fields[1]",issue.Path);
        Assert.False(string.IsNullOrWhiteSpace(issue.Message));
    }

    [Fact]
    public void A_block_with_its_questions_has_no_issue() =>
        Assert.Empty(FormPublicationPolicy.ValidateRows(Definition(Field("group",rows: new(1,20)),Field("decimal",repeated: true))));

    [Theory]
    [InlineData(20,true)]
    [InlineData(11,true)]
    [InlineData(10,false)]
    [InlineData(1,false)]
    public void A_maximum_over_the_channel_rows_warns_without_blocking(int maximum, bool warns)
    {
        var block = Field("group",rows: new(1,maximum));
        var definition = Definition(block,Field("decimal",repeated: true));

        var warnings = FormPublicationPolicy.RowWarnings(definition);

        Assert.Empty(FormPublicationPolicy.ValidateRows(definition));
        Assert.Equal(warns ? 1 : 0,warnings.Count);
        if (!warns) return;
        Assert.Equal(FormPublicationErrors.RowsOverChannelLimit.Code,warnings[0].Code);
        Assert.Equal(block.FieldId,warnings[0].FieldId);
        Assert.Equal("draft.fields[0].rows",warnings[0].Path);
        Assert.Equal(StructuredFormLimits.MaxRows,warnings[0].Limit);
        Assert.Equal(maximum,warnings[0].Maximum);
    }

    [Theory]
    [InlineData(15,true)]
    [InlineData(11,true)]
    [InlineData(10,false)]
    [InlineData(0,false)]
    public void A_minimum_over_the_channel_rows_blocks_publication_at_the_rows_of_the_block(int minimum, bool blocks)
    {
        var block = Field("group",rows: new(minimum,20));
        var definition = Definition(Field("shortText"),block,Field("decimal",repeated: true));

        var issues = FormPublicationPolicy.RowIssues(definition);

        Assert.Equal(blocks ? 1 : 0,issues.Count);
        if (!blocks) return;
        Assert.Equal(FormPublicationErrors.MinRowsOverChannelLimit.Code,issues[0].Code);
        Assert.Equal(block.FieldId,issues[0].FieldId);
        Assert.Equal("draft.fields[1].rows",issues[0].Path);
        Assert.Equal(StructuredFormLimits.MaxRows,issues[0].Limit);
        Assert.False(string.IsNullOrWhiteSpace(issues[0].Message));
    }

    [Fact]
    public void Questions_entering_the_block_pair_with_removed_ones_of_the_same_type_catalog_and_name()
    {
        var catalog = Guid.NewGuid();
        var oldQuantity = Field("decimal","Cantidad");
        var oldItem = Field("choice","Artículo",catalog: catalog);
        var oldRenamed = Field("shortText","Observación");
        var newQuantity = Field("decimal"," cantidad ",repeated: true);
        var newItem = Field("choice","Artículo",repeated: true,catalog: catalog);
        var newRenamed = Field("shortText","Observaciones",repeated: true);
        var loose = Field("decimal","Cantidad");
        var otherCatalog = Field("choice","Artículo",repeated: true,catalog: Guid.NewGuid());

        var pairs = FormPublicationPolicy.MovedToRows([oldQuantity,oldItem,oldRenamed],[otherCatalog,newQuantity,newItem,newRenamed,loose],"es");

        Assert.Equal(new[] { (newQuantity.FieldId,oldQuantity.FieldId),(newItem.FieldId,oldItem.FieldId) },pairs);
    }

    [Fact]
    public void Each_removed_question_pairs_with_one_question_entering_the_block()
    {
        var old = Field("decimal","Cantidad");
        var first = Field("decimal","Cantidad",repeated: true);
        var second = Field("decimal","Cantidad",repeated: true);

        Assert.Equal(new[] { (first.FieldId,old.FieldId) },FormPublicationPolicy.MovedToRows([old],[first,second],"es"));
    }

    [Fact]
    public void The_channel_request_carries_rows_and_repetition_and_omits_them_when_absent()
    {
        var questions = FormPublicationSnapshotPolicy.ChannelRequest(Definition(Field("shortText"),Field("group",rows: new(1,20)),
            Field("decimal",repeated: true))).Questions;

        Assert.Equal(new StructuredFormRowSettings(1,20),questions[1].Rows);
        Assert.True(questions[2].Repeated);
        Assert.Null(questions[0].Rows);
        Assert.Null(questions[0].Repeated);
        var json = FormDraftPolicy.Serialize(questions[0]);
        Assert.DoesNotContain("rows",json,StringComparison.Ordinal);
        Assert.DoesNotContain("repeated",json,StringComparison.Ordinal);
    }

    [Fact]
    public void Checking_publication_requires_the_saved_rows_and_repetition()
    {
        var request = FormDraftPolicy.Normalize(new SaveFormDraftRequest(Guid.NewGuid(),Text,"INS",new(1,"es",Text,
        [
            new("rows",null,null,"group",Text,null,true,null,null,Rows: new(1,20)),
            new("cantidad",null,null,"decimal",new Dictionary<string,string>{ ["es"]="Cantidad" },null,false,null,null,Repeated: true),
        ])));
        var resolution = FormDraftPolicy.Resolve(request,null,FormRetiredIdentities.Empty).Value;
        var saved = new FormDefinitionResponse(Guid.NewGuid(),request.Name,"INS",false,1,resolution.Draft);
        var input = request with { Draft = request.Draft with { Fields = request.Draft.Fields
            .Select((field,index) => field with { FieldId = resolution.Draft.Fields[index].FieldId }).ToArray() } };

        Assert.True(FormPublicationSnapshotPolicy.Matches(input,saved));
        Assert.False(FormPublicationSnapshotPolicy.Matches(input with { Draft = input.Draft with { Fields =
            [input.Draft.Fields[0] with { Rows = new(1,30) },input.Draft.Fields[1]] } },saved));
        Assert.False(FormPublicationSnapshotPolicy.Matches(input with { Draft = input.Draft with { Fields =
            [input.Draft.Fields[0],input.Draft.Fields[1] with { Repeated = null }] } },saved));
    }

    [Fact]
    public void The_block_is_not_counted_as_a_question() =>
        Assert.Equal(2,FormDraftPolicy.QuestionCount([Field("shortText"),Field("group",rows: new(0,5)),Field("decimal",repeated: true)]));
}
