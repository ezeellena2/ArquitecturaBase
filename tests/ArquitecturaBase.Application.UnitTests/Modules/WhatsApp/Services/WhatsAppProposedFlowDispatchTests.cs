using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppProposedFlowDispatchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static async Task<WhatsAppAssistantSuggestionFixture> QueuedAsync(string culture="es",bool all=false)
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync(culture);
        var fields=host.Forms.Prefill.Value.Fields;
        var sent=await host.Service.SendAsync(new(host.Context.Contact.Id,host.Request.RequestId,Guid.NewGuid(),
            all?fields.Select(field=>field.FieldId).ToArray():[fields[0].FieldId]),Ct);
        Assert.True(sent.IsSuccess);return host;
    }
    [Fact]
    public async Task Preparing_a_confirmed_proposal_uses_only_the_values_included_in_the_sealed_session()
    {
        var host=await QueuedAsync();var row=WhatsAppAssistantAcceptanceFixture.DispatchRow(host);
        var prepared=await WhatsAppAssistantAcceptanceFixture.CreateDispatch(host,row).PrepareAsync(row.OutgoingId,Ct);
        Assert.True(prepared.IsSuccess);Assert.Single(prepared.Value.InitialValues);
        var fields=prepared.Value.InitialValues["prefill_FORM"];Assert.Single(fields);
        Assert.Equal("valor privado",fields[host.Forms.Prefill.Value.Fields[0].Key]);
        Assert.False(fields.ContainsKey(host.Forms.Prefill.Value.Fields[1].Key));
    }
    [Fact]
    public async Task A_proposed_false_remains_a_wire_string_instead_of_becoming_an_absent_value()
    {
        var host=await QueuedAsync(all:true);var row=WhatsAppAssistantAcceptanceFixture.DispatchRow(host);
        var prepared=await WhatsAppAssistantAcceptanceFixture.CreateDispatch(host,row).PrepareAsync(row.OutgoingId,Ct);
        Assert.True(prepared.IsSuccess);Assert.Equal("false",prepared.Value.InitialValues["prefill_FORM"][host.Forms.Prefill.Value.Fields[1].Key]);
    }
    [Fact]
    public async Task The_owners_whole_language_fallback_survives_the_wait_in_the_outbox()
    {
        var host=await QueuedAsync("en");var row=WhatsAppAssistantAcceptanceFixture.DispatchRow(host);
        Assert.Equal("es",row.Culture);
        var prepared=await WhatsAppAssistantAcceptanceFixture.CreateDispatch(host,row).PrepareAsync(row.OutgoingId,Ct);
        Assert.True(prepared.IsSuccess);Assert.Equal("Te preparé el formulario para que lo completes.",prepared.Value.Body);
    }
    [Fact]
    public async Task An_inactive_operator_cannot_dispatch_a_previously_confirmed_manual_proposal()
    {
        var host=await QueuedAsync();await host.Context.Users.SetActiveAsync(host.Context.Operator.Id,false,Ct);
        var row=WhatsAppAssistantAcceptanceFixture.DispatchRow(host);
        Assert.True((await WhatsAppAssistantAcceptanceFixture.CreateDispatch(host,row).PrepareAsync(row.OutgoingId,Ct)).IsFailure);
    }
    [Fact]
    public async Task Source_retention_is_checked_before_reading_private_prefill_content()
    {
        var host=await QueuedAsync();var row=WhatsAppAssistantAcceptanceFixture.DispatchRow(host) with
            {AssistantContentExpiresAtUtc=host.Context.Clock.GetUtcNow().UtcDateTime};
        Assert.True((await WhatsAppAssistantAcceptanceFixture.CreateDispatch(host,row).PrepareAsync(row.OutgoingId,Ct)).IsFailure);
    }
    [Fact]
    public async Task An_explicit_automatic_session_uses_the_same_preparation_without_fabricating_an_operator()
    {
        var host=await QueuedAsync();var row=WhatsAppAssistantAcceptanceFixture.DispatchRow(host) with
            {Origin=WhatsAppMessageOrigin.Assistant,OperatorUserId=null,Mode=WhatsAppConversationMode.Assistant};
        var recipient=(await host.Context.Recipients.FindAsync(row.ContactId,Ct))!;
        host.Protector.Payload=host.Protector.Payload! with {Format=2,Origin=WhatsAppMessageOrigin.Assistant,OperatorUserId=null,
            RecipientRevision=WhatsAppFormRecipientRevision.Create(row.UserId,recipient)};
        host.Protector.Content=host.Protector.Content! with {Origin=host.Protector.Content.Origin with {SenderKind=StructuredRecordSenderKind.Assistant,OperatorUserId=null}};
        Assert.True((await WhatsAppAssistantAcceptanceFixture.CreateDispatch(host,row).PrepareAsync(row.OutgoingId,Ct)).IsSuccess);
    }
    [Fact]
    public async Task A_mismatching_private_sender_cannot_relabel_the_sealed_manual_session()
    {
        var host=await QueuedAsync();var row=WhatsAppAssistantAcceptanceFixture.DispatchRow(host);
        host.Protector.Content=host.Protector.Content! with {Origin=host.Protector.Content.Origin with {OperatorUserId=Guid.NewGuid()}};
        Assert.True((await WhatsAppAssistantAcceptanceFixture.CreateDispatch(host,row).PrepareAsync(row.OutgoingId,Ct)).IsFailure);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Forms_without_included_answers_omit_initial_value_maps(bool reviewedSuggestion)
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();
        var sent=await host.Service.SendAsync(new(host.Context.Contact.Id,host.Request.RequestId,Guid.NewGuid(),[]),Ct);
        Assert.True(sent.IsSuccess);
        var row=WhatsAppAssistantAcceptanceFixture.DispatchRow(host);
        if(!reviewedSuggestion) row=row with {ProtectedAssistantContent=null,AssistantContentExpiresAtUtc=null};
        var prepared=await WhatsAppAssistantAcceptanceFixture.CreateDispatch(host,row).PrepareAsync(row.OutgoingId,Ct);
        Assert.True(prepared.IsSuccess);
        Assert.Empty(prepared.Value.InitialValues);
    }
    [Fact]
    public async Task A_legacy_session_without_assistant_content_keeps_its_existing_empty_initial_data()
    {
        var host=await QueuedAsync();var row=WhatsAppAssistantAcceptanceFixture.DispatchRow(host) with
            {ProtectedAssistantContent=null,AssistantContentExpiresAtUtc=null,ProtectedProjection=null};
        var prepared=await WhatsAppAssistantAcceptanceFixture.CreateDispatch(host,row).PrepareAsync(row.OutgoingId,Ct);
        Assert.True(prepared.IsSuccess);Assert.Empty(prepared.Value.InitialValues);
    }
}
