using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormSuggestionPolicyTests
{
    [Fact]
    public void Empty_configuration_disables_suggestions_without_a_publication_change()
    {
        var result=FormSuggestionPolicy.Normalize(new(0, []), Context());
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Order_and_identity_are_preserved_and_instruction_text_needs_only_one_language()
    {
        var first = Suggestion() with { When = "  cuando pida factura  " };
        var second = Suggestion() with { IsActive = false, Mode = "Always", When = null };
        var result = FormSuggestionPolicy.Normalize(new(3, [first,second]), Context());
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { first.Id, second.Id }, result.Value.Select(suggestion => suggestion.Id));
        Assert.Equal("cuando pida factura", result.Value[0].When);
        Assert.False(result.Value[1].IsActive);
        Assert.Null(result.Value[1].When);
    }

    [Theory]
    [InlineData("shortText")]
    [InlineData("longText")]
    [InlineData("decimal")]
    [InlineData("date")]
    [InlineData("boolean")]
    [InlineData("choice")]
    public void Equal_scalar_types_can_be_mapped(string type)
    {
        var mapping = new FormSuggestionMapping(new("Field", SourceFieldId), TargetFieldId);
        var suggestion = Suggestion() with { Mappings = [mapping] };
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[suggestion]), Context(type,type)).IsSuccess);
    }

    [Theory]
    [InlineData("shortText", "longText")]
    [InlineData("longText", "shortText")]
    [InlineData("decimal", "shortText")]
    [InlineData("photo", "photo")]
    [InlineData("document", "document")]
    public void Different_types_and_files_cannot_be_mapped(string sourceType, string targetType)
    {
        var suggestion = Suggestion() with { Mappings = [new(new("Field",SourceFieldId),TargetFieldId)] };
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[suggestion]), Context(sourceType,targetType)).IsFailure);
    }

    [Theory]
    [InlineData("shortText", true)]
    [InlineData("longText", true)]
    [InlineData("decimal", false)]
    [InlineData("date", false)]
    [InlineData("boolean", false)]
    [InlineData("choice", false)]
    public void Record_number_is_metadata_and_only_targets_text(string targetType, bool valid)
    {
        var suggestion = Suggestion() with { Mappings = [new(new("RecordNumber"),TargetFieldId)] };
        Assert.Equal(valid,FormSuggestionPolicy.Normalize(new(0,[suggestion]),Context("shortText",targetType)).IsSuccess);
    }

    [Theory]
    [InlineData("same", true)]
    [InlineData("different", false)]
    [InlineData("inline", false)]
    [InlineData("sourceOnly", false)]
    [InlineData("targetOnly", false)]
    public void Choice_mapping_requires_the_same_explicit_catalog(string relationship, bool valid)
    {
        var context = Context("choice", "choice");
        var catalog = Guid.NewGuid();
        var source = context.Source.Questions[0] with { Source = relationship is "inline" or "targetOnly" ? null : new(catalog) };
        var target = context.Destinations[0].Questions[0] with { Source = relationship is "inline" or "sourceOnly" ? null
            : new(relationship == "different" ? Guid.NewGuid() : catalog) };
        context = context with { Source = context.Source with { Questions = [source] },
            Destinations = [context.Destinations[0] with { Questions = [target] }] };
        var suggestion = Suggestion() with { Mappings = [new(new("Field", SourceFieldId), TargetFieldId)] };
        var result = FormSuggestionPolicy.Normalize(new(0, [suggestion]), context);
        Assert.Equal(valid, result.IsSuccess);
        if (!valid) Assert.Contains("suggestions[0].mappings[0].source", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
    }

    [Fact]
    public void Record_number_cannot_smuggle_a_field_id()
    {
        var suggestion = Suggestion() with { Mappings = [new(new("RecordNumber",SourceFieldId),TargetFieldId)] };
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[suggestion]),Context()).IsFailure);
    }

    [Fact]
    public void A_destination_field_can_receive_only_one_mapping()
    {
        var mapping = new FormSuggestionMapping(new("Field",SourceFieldId),TargetFieldId);
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[Suggestion() with { Mappings = [mapping,mapping] }]),Context()).IsFailure);
    }

    [Fact]
    public void Unknown_fields_and_cross_form_ids_are_rejected()
    {
        var sourceUnknown = Suggestion() with { Mappings = [new(new("Field",Guid.NewGuid()),TargetFieldId)] };
        var targetUnknown = Suggestion() with { Mappings = [new(new("Field",SourceFieldId),Guid.NewGuid())] };
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[sourceUnknown]),Context()).IsFailure);
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[targetUnknown]),Context()).IsFailure);
    }

    [Fact]
    public void Destinations_are_published_and_cannot_be_the_source_form()
    {
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[Suggestion()]),Context() with { Destinations = [] }).IsFailure);
        var source = Context().Source;
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[Suggestion() with { DestinationDefinitionId = SourceId }]),
            Context() with { Destinations = [source] }).IsFailure);
    }

    [Fact]
    public void Assistant_requires_instruction_text_but_always_does_not_call_for_interpretation()
    {
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[Suggestion() with { When = "  " }]),Context()).IsFailure);
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[Suggestion() with { Mode = "Always", When = " " }]),Context()).IsSuccess);
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[Suggestion() with { Mode = "Automatic" }]),Context()).IsFailure);
    }

    [Fact]
    public void Always_ignores_instruction_text_and_persists_only_its_typed_condition()
    {
        var suggestion=Suggestion() with {Mode="Always",When="No debe interpretarse ni guardarse"};
        var result=FormSuggestionPolicy.Normalize(new(0,[suggestion]),Context());
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value[0].When);
    }

    [Fact]
    public void Conditions_reuse_the_typed_record_grammar()
    {
        var valid = Suggestion() with { Conditions = [new(SourceFieldId,"in",["false"])] };
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[valid]),Context("boolean","boolean")).IsSuccess);
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[valid]),Context("shortText","shortText")).IsFailure);
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[valid with { Conditions = [new(Guid.NewGuid(),"answered",[])] }]),Context()).IsFailure);
    }

    [Fact]
    public void Control_characters_and_oversized_instruction_text_are_rejected_without_truncation()
    {
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[Suggestion() with { When = "hola" + (char)0 }]),Context()).IsFailure);
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[Suggestion() with { When = new string('a',1001) }]),Context()).IsFailure);
        var bounded=FormSuggestionPolicy.Normalize(new(0,[Suggestion() with { When = new string('a',1000) }]),Context());
        Assert.True(bounded.IsSuccess);
        Assert.Equal(1000,bounded.Value[0].When!.Length);
    }

    [Fact]
    public void Invalid_revisions_duplicate_ids_and_over_budget_lists_are_rejected()
    {
        var suggestion = Suggestion();
        Assert.True(FormSuggestionPolicy.Normalize(new(null,[suggestion]),Context()).IsFailure);
        Assert.True(FormSuggestionPolicy.Normalize(new(-1,[suggestion]),Context()).IsFailure);
        Assert.True(FormSuggestionPolicy.Normalize(new(9007199254740991,[suggestion]),Context()).IsFailure);
        Assert.True(FormSuggestionPolicy.Normalize(new(0,[suggestion,suggestion]),Context()).IsFailure);
        Assert.True(FormSuggestionPolicy.Normalize(new(0,Enumerable.Range(0,21).Select(_=>(FormSuggestionInput?)Suggestion()).ToArray()),Context()).IsFailure);
    }

    [Fact]
    public void Normalization_copies_nested_collections()
    {
        var values = new List<string> { "false" };
        var mappings = new List<FormSuggestionMapping?> { new(new("Field",SourceFieldId),TargetFieldId) };
        var suggestion = Suggestion() with { Conditions = [new(SourceFieldId,"in",values)], Mappings = mappings };
        var result = FormSuggestionPolicy.Normalize(new(0,[suggestion]),Context("boolean","boolean"));
        Assert.True(result.IsSuccess);
        var normalized=result.Value[0];
        values[0] = "true";
        mappings.Clear();
        Assert.Equal("false",normalized.Conditions[0].Values![0]);
        Assert.Single(normalized.Mappings);
    }

    private static readonly Guid SharedCatalogId = Guid.NewGuid();
    private static readonly Guid SourceId = Guid.NewGuid();
    private static readonly Guid DestinationId = Guid.NewGuid();
    private static readonly Guid SourceFieldId = Guid.NewGuid();
    private static readonly Guid TargetFieldId = Guid.NewGuid();
    private static FormSuggestionInput Suggestion() => new(Guid.NewGuid(),DestinationId,"Assistant","Cuando pida factura",true,[],[]);
    private static CanonicalFormField Field(Guid id,string type) => new(id,"field_"+id.ToString("N"),type,
        new Dictionary<string,string> { ["es"] = "Pregunta" },null,false,type is "shortText" or "longText" ? 80 : null,null, type == "choice" ? new(SharedCatalogId) : null);
    private static FormSuggestionOptionsResponse Context(string sourceType="shortText",string targetType="shortText") => new(
        new(SourceId,Guid.NewGuid(),new Dictionary<string,string> { ["es"] = "Entrega" },"es",[Field(SourceFieldId,sourceType)]),
        [new(DestinationId,Guid.NewGuid(),new Dictionary<string,string> { ["es"] = "Factura" },"es",[Field(TargetFieldId,targetType)])]);
}
