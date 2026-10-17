using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormRecordCaptureOriginPolicyTests
{
    [Fact]
    public async Task Missing_origin_stays_unknown_without_looking_up_a_record()
    {
        var f=new FormRecordCaptureOriginFixture();var result=await f.Policy.ValidateAsync(f.Request,f.Schema,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Null(result.Value);Assert.Equal(0,f.Responses.Reads);
    }
    [Fact]
    public async Task Manual_origin_preserves_the_actual_operator_without_requiring_a_proposal()
    {
        var f=new FormRecordCaptureOriginFixture();var result=await f.Policy.ValidateAsync(f.Request with{Origin=f.Manual()},f.Schema,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.NotNull(result.Value);Assert.Equal(f.OperatorId,result.Value.Origin.OperatorUserId);Assert.Null(result.Value.Origin.Proposal);
    }
    [Fact]
    public async Task Assistant_origin_preserves_exact_provenance_and_does_not_invent_an_operator()
    {
        var f=new FormRecordCaptureOriginFixture();var origin=f.Assistant();var result=await f.Policy.ValidateAsync(f.Request with{Origin=origin},f.Schema,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.NotNull(result.Value);Assert.Null(result.Value.Origin.OperatorUserId);Assert.Equal(origin.Proposal,result.Value.Origin.Proposal);
        Assert.Empty(result.Value.ChangedFieldIds);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sender_identity_is_explicit_and_cannot_be_deduced_from_missing_operator(bool assistant)
    {
        var f=new FormRecordCaptureOriginFixture();var invalid=assistant?f.Assistant() with{OperatorUserId=f.OperatorId}:f.Manual() with{OperatorUserId=null};
        Assert.True((await f.Policy.ValidateAsync(f.Request with{Origin=invalid},f.Schema,TestContext.Current.CancellationToken)).IsFailure);
    }
    [Theory]
    [InlineData("local","local",false)]
    [InlineData("local","otro",true)]
    [InlineData("local","",true)]
    public async Task Included_values_record_whether_the_person_changed_or_removed_them(string proposed,string final,bool changed)
    {
        var f=new FormRecordCaptureOriginFixture(final:final);var result=await f.Policy.ValidateAsync(f.Request with{Origin=f.Assistant(proposed)},f.Schema,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.NotNull(result.Value);Assert.Equal(changed,result.Value.ChangedFieldIds.Contains(f.Field.FieldId));
    }
    [Fact]
    public async Task An_operator_excluded_value_is_not_claimed_as_changed_or_mapped()
    {
        var f=new FormRecordCaptureOriginFixture(final:"otro");var result=await f.Policy.ValidateAsync(f.Request with{Origin=f.Assistant("local") with{SenderKind=StructuredRecordSenderKind.Manual,OperatorUserId=f.OperatorId,IncludedFieldIds=[]}},f.Schema,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.NotNull(result.Value);Assert.Empty(result.Value.ChangedFieldIds);Assert.Empty(result.Value.Origin.IncludedFieldIds);
    }
    [Fact]
    public async Task Decimal_comparison_uses_canonical_values_without_marking_formatting_as_a_change()
    {
        var f=new FormRecordCaptureOriginFixture("decimal","1","1.00");var result=await f.Policy.ValidateAsync(f.Request with{Origin=f.Assistant("1")},f.Schema,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.NotNull(result.Value);Assert.Empty(result.Value.ChangedFieldIds);
    }
    [Fact]
    public async Task Duplicate_or_unproposed_included_fields_are_rejected()
    {
        var f=new FormRecordCaptureOriginFixture();var origin=f.Assistant();
        Assert.True((await f.Policy.ValidateAsync(f.Request with{Origin=origin with{ProposedValues=[origin.ProposedValues[0],origin.ProposedValues[0]]}},f.Schema,TestContext.Current.CancellationToken)).IsFailure);
        Assert.True((await f.Policy.ValidateAsync(f.Request with{Origin=origin with{IncludedFieldIds=[Guid.NewGuid()]}},f.Schema,TestContext.Current.CancellationToken)).IsFailure);
    }
    [Fact]
    public async Task Proposed_fields_must_belong_to_the_sealed_destination_schema_and_have_its_type()
    {
        var f=new FormRecordCaptureOriginFixture();var origin=f.Assistant();
        Assert.True((await f.Policy.ValidateAsync(f.Request with{Origin=origin with{ProposedValues=[origin.ProposedValues[0] with{FieldId=Guid.NewGuid()}]}},f.Schema,TestContext.Current.CancellationToken)).IsFailure);
        Assert.True((await f.Policy.ValidateAsync(f.Request with{Origin=origin with{ProposedValues=[origin.ProposedValues[0] with{Type="longText"}]}},f.Schema,TestContext.Current.CancellationToken)).IsFailure);
    }
    [Fact]
    public async Task Incomplete_provenance_and_nonstorable_reasons_are_rejected()
    {
        var f=new FormRecordCaptureOriginFixture();var origin=f.Assistant();
        Assert.True((await f.Policy.ValidateAsync(f.Request with{Origin=origin with{Proposal=origin.Proposal! with{CapabilityVersion=0}}},f.Schema,TestContext.Current.CancellationToken)).IsFailure);
        Assert.True((await f.Policy.ValidateAsync(f.Request with{Origin=origin with{Reason="bad"+(char)0}},f.Schema,TestContext.Current.CancellationToken)).IsFailure);
    }
    [Fact]
    public async Task Always_suggestion_captures_lineage_without_ever_requiring_model_provenance()
    {
        var f=new FormRecordCaptureOriginFixture();var origin=f.Lineage();var result=await f.Policy.ValidateAsync(f.Request with{Origin=origin},f.Schema,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.NotNull(result.Value);Assert.Equal(origin.Suggestion,result.Value.Origin.Suggestion);Assert.Null(result.Value.Origin.Proposal);
    }
    [Fact]
    public async Task A_lineage_from_another_person_is_rejected_without_a_number_or_values()
    {
        var f=new FormRecordCaptureOriginFixture();var origin=f.Lineage();f.Responses.ExportRows=[f.Responses.ExportRows[0] with{Actor=new(Guid.NewGuid(),"Other")}];
        Assert.True((await f.Policy.ValidateAsync(f.Request with{Origin=origin},f.Schema,TestContext.Current.CancellationToken)).IsFailure);
    }
    [Fact]
    public async Task A_suggestion_not_in_the_fixed_revision_is_rejected()
    {
        var f=new FormRecordCaptureOriginFixture();var origin=f.Lineage();origin=origin with{Suggestion=origin.Suggestion! with{OriginSuggestionId=Guid.NewGuid()}};
        Assert.True((await f.Policy.ValidateAsync(f.Request with{Origin=origin},f.Schema,TestContext.Current.CancellationToken)).IsFailure);
    }
    [Fact]
    public async Task A_mapping_cannot_claim_a_different_source_field_or_value()
    {
        var f=new FormRecordCaptureOriginFixture();var origin=f.Lineage();
        Assert.True((await f.Policy.ValidateAsync(f.Request with{Origin=origin with{Suggestion=origin.Suggestion! with{Mappings=[new("Field",Guid.NewGuid(),f.Field.FieldId)]}}},f.Schema,TestContext.Current.CancellationToken)).IsFailure);
        Assert.True((await f.Policy.ValidateAsync(f.Request with{Origin=origin with{ProposedValues=[origin.ProposedValues[0] with{Value="tampered"}]}},f.Schema,TestContext.Current.CancellationToken)).IsFailure);
    }
    [Fact]
    public async Task Assistant_lineage_keeps_the_exact_suggestion_proposal_capability()
    {
        var f = new FormRecordCaptureOriginFixture(); var origin = f.Lineage("Assistant");
        origin = origin with { Proposal = origin.Proposal! with { CapabilityId = "forms.propose-suggestion", CapabilityVersion = 1 } };
        var result = await f.Policy.ValidateAsync(f.Request with { Origin = origin }, f.Schema, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task Assistant_lineage_cannot_reuse_a_general_registration_proposal()
    {
        var f = new FormRecordCaptureOriginFixture(); var origin = f.Lineage("Assistant");
        var result = await f.Policy.ValidateAsync(f.Request with { Origin = origin }, f.Schema, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
    }

}
