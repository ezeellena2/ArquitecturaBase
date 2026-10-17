using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppConfiguredSuggestionTests
{
    private static readonly DateTime Now=new(2026,10,17,15,0,0,DateTimeKind.Utc);
    private static WhatsAppAssistantRequest Create()=>WhatsAppAssistantRequest.CreateConfiguredManual(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
        Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"es",3,7,Now,Now.AddDays(1));
    [Fact]
    public void A_manual_configured_offer_does_not_invent_an_actor_a_model_context_or_a_proposal()
    {
        var row=Create();Assert.Null(row.ActorUserId);Assert.Null(row.ContextJson);Assert.Null(row.ProposalId);Assert.True(row.IsAlways);
        Assert.NotNull(row.SourceRecordId);Assert.NotNull(row.SuggestionSetId);Assert.NotNull(row.SuggestionId);Assert.Equal("es",row.CapturedCulture);
    }
    [Fact]
    public void A_validated_always_offer_becomes_ready_with_no_model_proposal()
    {
        var row=Create();var definition=Guid.NewGuid();Assert.True(row.Ready(null,definition,Now));Assert.Null(row.ProposalId);
        Assert.True(row.Ready(null,definition,Now.AddMinutes(1)));Assert.False(row.Ready(Guid.NewGuid(),definition,Now));
    }
    [Fact]
    public void Confirming_always_attributes_the_real_human_and_preserves_absence_of_AI()
    {
        var row=Create();Assert.True(row.Ready(null,Guid.NewGuid(),Now));var actor=Guid.NewGuid();
        Assert.True(row.Accept(Guid.NewGuid(),new string('a',64),actor,Now));Assert.Equal(actor,row.DecidedByUserId);Assert.Null(row.ProposalId);
    }
    [Fact]
    public void A_configured_capture_and_suggestion_cannot_be_rebound_after_acceptance()
    {
        var row=Create();Assert.True(row.SetCapturedSuggestion(row.SourceRecordId!.Value,row.SuggestionSetId!.Value,row.SuggestionId!.Value,"es",true,Now));
        Assert.False(row.SetCapturedSuggestion(Guid.NewGuid(),row.SuggestionSetId.Value,row.SuggestionId.Value,"es",true,Now));
        Assert.False(row.SetCapturedSuggestion(row.SourceRecordId.Value,Guid.NewGuid(),row.SuggestionId.Value,"es",true,Now));
    }
    [Fact]
    public void A_normal_model_reference_cannot_become_ready_without_a_real_proposal()
    {
        var row=WhatsAppAssistantRequest.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),WhatsAppAssistantRequestKind.Manual,0,0,"{}",Now,Now.AddDays(1));
        Assert.False(row.Ready(null,Guid.NewGuid(),Now));Assert.False(row.SetCapturedSuggestion(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"es",true,Now.AddDays(1)));
    }
}
