using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Publishing section titles (CW15-B): a title with no question below blocks, a missing translation leaves that language
// incomplete and the channel receives the titles with the questions.
public sealed class FormSectionPublicationTests
{
    private static readonly Dictionary<string,string> Text = new() { ["es"]="Aplicación", ["en"]="Application" };

    private static CanonicalFormField Field(string label) =>
        new(Guid.CreateVersion7(),"field_x","shortText",new Dictionary<string,string>{ ["es"]=label, ["en"]=label },null,false,80,null);

    private static FormDefinitionResponse Definition(IReadOnlyList<FormSection>? sections, params CanonicalFormField[] fields) =>
        new(Guid.NewGuid(),Text,"APL",false,1,new FormDraftResponse(1,"es",Text,fields,sections));

    private static FormSection Section(int start, string es, string? en = null) =>
        new(start,en is null ? new Dictionary<string,string> { ["es"]=es } : new Dictionary<string,string> { ["es"]=es, ["en"]=en });

    [Fact]
    public void A_title_with_no_question_below_blocks_publication_at_the_title()
    {
        var definition = Definition([Section(0,"Lote","Lot"),Section(2,"Vacío","Empty"),Section(2,"Productos","Products"),Section(3,"Al final","At the end")],
            Field("Lote"),Field("Hectáreas"),Field("Producto"));

        var issues = FormPublicationPolicy.ValidateSections(definition);

        Assert.Equal(new[] { "draft.sections[1]","draft.sections[3]" },issues.Select(issue => issue.Path));
        Assert.All(issues,issue =>
        {
            Assert.Equal(FormPublicationErrors.SectionEmpty.Code,issue.Code);
            Assert.Null(issue.FieldId);
            Assert.False(string.IsNullOrWhiteSpace(issue.Message));
        });
    }

    [Fact]
    public void Titles_with_questions_below_and_a_form_without_titles_have_no_issue()
    {
        Assert.Empty(FormPublicationPolicy.ValidateSections(Definition([Section(0,"Lote","Lot"),Section(2,"Productos","Products")],Field("Lote"),Field("Hectáreas"),Field("Producto"))));
        Assert.Empty(FormPublicationPolicy.ValidateSections(Definition(null,Field("Lote"))));
    }

    [Fact]
    public void A_title_without_its_translation_leaves_that_language_incomplete()
    {
        var cultures = FormPublicationPolicy.CheckCultures(Definition([Section(0,"Lote","Lot"),Section(1,"Productos")],Field("Lote"),Field("Producto")));

        var english = Assert.Single(cultures,culture => culture.Culture == "en");
        Assert.False(english.IsComplete);
        var missing = Assert.Single(english.Missing);
        Assert.Equal("draft.sections[1].title.en",missing.Path);
        Assert.True(Assert.Single(cultures,culture => culture.Culture == "es").IsComplete);
    }

    [Fact]
    public void The_channel_receives_the_titles_with_the_questions_and_nothing_without_them()
    {
        var request = FormPublicationSnapshotPolicy.ChannelRequest(Definition([Section(1,"Productos","Products")],Field("Lote"),Field("Producto")));
        var without = FormPublicationSnapshotPolicy.ChannelRequest(Definition(null,Field("Lote")));

        var section = Assert.Single(request.Sections!);
        Assert.Equal(1,section.Start);
        Assert.Equal("Products",section.Title["en"]);
        Assert.Null(without.Sections);
        Assert.DoesNotContain("sections",FormDraftPolicy.Serialize(without),StringComparison.Ordinal);
    }

    [Fact]
    public void A_sealed_version_keeps_its_titles_and_one_without_them_keeps_its_bytes()
    {
        var fields = new[] { Field("Lote") };
        var sealedSchema = FormDraftPolicy.Serialize(new FormVersionSchema(1,fields,[Section(0,"Lote","Lot")]));

        var read = FormDraftPolicy.Read<FormVersionSchema>(sealedSchema);

        Assert.Equal(sealedSchema,FormDraftPolicy.Serialize(read));
        Assert.Equal(0,Assert.Single(read.Sections!).Start);
        Assert.Equal(FormDraftPolicy.Serialize(new { format = 1, questions = fields }),FormDraftPolicy.Serialize(new FormVersionSchema(1,fields)));
    }
}
