using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormSuggestionMappingPolicyTests
{
    [Theory]
    [InlineData("shortText", "local A", "local A")]
    [InlineData("longText", "observación", "observación")]
    [InlineData("decimal", "0", "0")]
    [InlineData("decimal", "12.50", "12.5")]
    [InlineData("date", "2026-10-17", "2026-10-17")]
    [InlineData("boolean", "false", "false")]
    public void Historical_scalar_values_are_normalized_without_losing_zero_or_false(string type,string raw,string expected)
    {
        object value = type switch { "decimal" => decimal.Parse(raw,System.Globalization.CultureInfo.InvariantCulture), "boolean" => bool.Parse(raw), _ => raw };
        var actual = FormSuggestionMappingPolicy.Map(Suggestion(),Source(type,value),[Target(type)],[]);
        Assert.True(actual.TryGetValue(TargetKey,out var mapped));
        Assert.Equal(expected,mapped);
    }
    [Fact]
    public void Record_number_comes_from_record_metadata()
    {
        var suggestion = Suggestion() with { Mappings = [new(new("RecordNumber"),TargetId)] };
        var actual = FormSuggestionMappingPolicy.Map(suggestion,Source("shortText","cliente"),[Target("shortText")],[]);
        Assert.True(actual.TryGetValue(TargetKey,out var mapped));
        Assert.Equal("ENT-000012",mapped);
    }
    [Fact]
    public void Missing_optional_values_do_not_become_empty_or_false()
    {
        var source = Source("boolean",false) with { ValuesJson = "[]" };
        Assert.Empty(FormSuggestionMappingPolicy.Map(Suggestion(),source,[Target("boolean")],[]));
    }
    [Fact]
    public void A_field_not_present_in_the_source_version_is_omitted()
    {
        var source = Source("shortText","a") with { SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1,[])) };
        Assert.Empty(FormSuggestionMappingPolicy.Map(Suggestion(),source,[Target("shortText")],[]));
    }
    [Fact]
    public void Destination_constraints_are_revalidated_without_truncation()
    {
        var actual = FormSuggestionMappingPolicy.Map(Suggestion(),Source("shortText","demasiado largo"),[Target("shortText") with { MaxLength = 4 }],[]);
        Assert.Empty(actual);
    }
    [Fact]
    public void A_choice_is_not_translated_by_label_or_copied_if_not_offered_to_this_recipient()
    {
        var sourceChoice = Guid.NewGuid();
        var target = Target("choice") with { Options = [new(Guid.NewGuid(),new Dictionary<string,string> { ["es"] = "Igual" })] };
        Assert.Empty(FormSuggestionMappingPolicy.Map(Suggestion(),Source("choice",sourceChoice),[target],[]));
    }
    [Fact]
    public void A_choice_shared_by_the_destination_catalog_is_preserved_by_id()
    {
        var choice = Guid.NewGuid();
        var catalog = Guid.NewGuid();
        var target = Target("choice") with { SourceCatalogId = catalog };
        var offer = new StructuredFormCatalogOffer(TargetId,TargetKey,"Cliente",catalog,2,"Manual","Clientes",[new(choice,"Cliente",null)]);
        var actual=FormSuggestionMappingPolicy.Map(Suggestion(),Source("choice",choice),[target],[offer]);
        Assert.True(actual.TryGetValue(TargetKey,out var mapped));
        Assert.Equal(choice.ToString("D"),mapped);
    }
    [Fact]
    public void Photos_and_documents_are_never_copied()
    {
        Assert.Empty(FormSuggestionMappingPolicy.Map(Suggestion(),Source("photo","private"),[Target("photo")],[]));
        Assert.Empty(FormSuggestionMappingPolicy.Map(Suggestion(),Source("document","private"),[Target("document")],[]));
    }
    private static readonly Guid SourceId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();
    private static string TargetKey => "field_"+TargetId.ToString("N");
    private static FormSuggestion Suggestion() => new(Guid.NewGuid(),Guid.NewGuid(),"Always",null,true,[],[new(new("Field",SourceId),TargetId)]);
    private static StructuredFormQuestion Target(string type) => new(TargetId,TargetKey,type,new Dictionary<string,string> { ["es"] = "Destino" },null,false,type is "shortText" or "longText" ? 80 : null,null);
    private static FormRecordStoredRow Source(string type,object value)
    {
        var field = new CanonicalFormField(SourceId,"field_"+SourceId.ToString("N"),type,new Dictionary<string,string> { ["es"] = "Origen" },null,false,type is "shortText" or "longText" ? 80 : null,null);
        return new(Guid.NewGuid(),Guid.NewGuid(),12,Guid.NewGuid(),"ENT",Guid.NewGuid(),1,"{}",
            FormDraftPolicy.Serialize(new FormVersionSchema(1,[field])),FormDraftPolicy.Serialize(new[] { new { FieldId = SourceId, Value = value } }));
    }
}
