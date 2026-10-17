using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFlowAssistantSessionTests
{
    private static readonly DateTime Now=new(2026,10,17,15,0,0,DateTimeKind.Utc);
    private static Guid? Operator(WhatsAppFlowSession session)=>session.OperatorUserId;
    private static WhatsAppFlowSession Manual()=>WhatsAppFlowSession.Create(Guid.NewGuid(),2,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),1,Guid.NewGuid(),"es","{}","V",new('a',64),new('b',64),Now.AddHours(1));

    [Fact]
    public void An_automatic_session_has_an_explicit_assistant_origin_and_no_fabricated_operator()
    {
        var session=WhatsAppFlowSession.CreateAssistant(Guid.NewGuid(),2,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),1,Guid.NewGuid(),"es","{}","V",new('a',64),new('b',64),Now.AddHours(1));
        Assert.Equal(WhatsAppMessageOrigin.Assistant,session.Origin); Assert.Null(Operator(session));
    }
    [Fact]
    public void Existing_manual_sessions_preserve_the_real_operator_and_conversation_origin()
    {
        var session=Manual(); Assert.Equal(WhatsAppMessageOrigin.Conversation,session.Origin); Assert.NotEqual(Guid.Empty,session.OperatorUserId);
    }
    [Fact]
    public void Private_proposal_content_is_separate_from_the_legacy_token_payload_and_is_sealed_once()
    {
        var session=Manual(); session.SealPayload([1,2,3]);
        Assert.True(session.SealAssistantContent([4,5,6],Now.AddMinutes(30)));
        Assert.False(session.SealAssistantContent([7],Now.AddMinutes(30)));
        Assert.Equal(new byte[]{1,2,3},session.ProtectedPayload); Assert.Equal(new byte[]{4,5,6},session.ProtectedAssistantContent);
    }
    [Fact]
    public void Expiry_clears_only_private_assistant_content_using_the_owner_lifetime()
    {
        var session=Manual(); session.SealPayload([1,2,3]);
        Assert.True(session.SealAssistantContent([4,5],Now.AddMinutes(30)));
        Assert.False(session.ClearAssistantContent(Now.AddMinutes(29)));
        Assert.True(session.ClearAssistantContent(Now.AddMinutes(30)));
        Assert.Empty(session.ProtectedAssistantContent); Assert.Equal(new byte[]{1,2,3},session.ProtectedPayload);
        Assert.False(session.ClearAssistantContent(Now.AddHours(1)));
    }
    [Fact]
    public void Private_content_preserves_the_owner_retention_beyond_token_expiry_and_requires_a_valid_blob()
    {
        var session=Manual();
        Assert.False(session.SealAssistantContent([],Now.AddMinutes(30)));
        Assert.True(session.SealAssistantContent([4],Now.AddHours(2)));
        Assert.False(session.ClearAssistantContent(Now.AddHours(1)));
        Assert.False(session.SealAssistantContent([4],DateTime.SpecifyKind(Now.AddMinutes(30),DateTimeKind.Unspecified)));
    }
}
