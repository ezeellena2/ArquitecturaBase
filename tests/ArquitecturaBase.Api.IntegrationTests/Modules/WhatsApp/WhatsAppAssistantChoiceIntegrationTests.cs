using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantChoiceIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_second_daily_question_and_its_chosen_flow_share_one_offer_and_replay_never_submits_another_operation()
    {
        await using var host=await WhatsAppAssistantScenario.CreateAsync(factory,Ct);
        var contact=await host.ContactAsync(Ct,automatic:true);
        var first=await PrepareAsync(host,contact.ContactId);
        await host.CompleteOutcomeAsync(contact.ContactId,host.Outcome(contact,first,"ready"),Ct);
        await SendNextAsync(host);
        contact=contact with {SourceMessageId=await host.AddNewerSourceAsync(contact,Ct)};
        var question=await QuestionAsync(host,contact); await host.CompleteOutcomeAsync(contact.ContactId,question,Ct);
        Assert.Equal((2,1),await CountsAsync(host,contact.ContactId)); await SendNextAsync(host);
        var offers=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().ListOffersAsync(contact.RecipientId,Ct));
        Assert.Contains(offers,offer=>offer.RequestId==question.RequestId && offer.DefinitionId==host.Forms.Form.DefinitionId);
        Assert.Contains(offers,offer=>offer.RequestId==question.RequestId && offer.DefinitionId==host.Forms.AlternativeForm.DefinitionId);
        var button=await host.ChooseAsync(contact,question.RequestId,host.Forms.Form.DefinitionId,host.Forms.Form.VersionId,Ct);
        var selected=await PrepareAsync(host,contact.ContactId); Assert.False(selected.ShouldSubmit); Assert.Equal(question.RequestId,selected.RequestId);
        var processor=host.Host.Services.GetRequiredService<WhatsAppInboundProcessor>();
        await processor.ProcessPendingAsync(Ct); await processor.ProcessPendingAsync(Ct);
        Assert.Equal((3,2),await CountsAsync(host,contact.ContactId));Assert.Empty(host.Operations.SubmitRequests);
        var observedContext=Assert.Single(host.Operations.ReadContexts);
        Assert.Equal(contact.RecipientId,observedContext.ActorUserId);Assert.Equal(contact.RecipientId,observedContext.SubjectUserId);
        var row=await host.ScopeAsync(services=>services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .SingleAsync(item=>item.RequestId==question.RequestId,Ct));
        Assert.Equal(WhatsAppAssistantRequestState.Sent,row.State);Assert.NotNull(row.QuestionOutgoingId);Assert.NotNull(row.OutgoingId);
        Assert.NotEqual(row.QuestionOutgoingId,row.OutgoingId);
        var flowRequest=await host.ScopeAsync(services=>services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(item=>item.Id==row.OutgoingId).Select(item=>item.RequestId).SingleAsync(Ct));
        Assert.NotEqual(question.RequestId,flowRequest);
        var count=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().CountOffersAsync(contact.ContactId,
            host.Clock.GetUtcNow().UtcDateTime.AddDays(-1),host.Clock.GetUtcNow().UtcDateTime.AddDays(1),Ct));Assert.Equal(2,count);
        Assert.True(await host.ScopeAsync(services=>services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .AnyAsync(item=>item.Id==button && item.AssistantProcessedAtUtc!=null,Ct)));
    }

    [Fact]
    public async Task A_wrong_version_button_is_consumed_without_preventing_a_later_valid_selection()
    {
        await using var host=await WhatsAppAssistantScenario.CreateAsync(factory,Ct);var contact=await host.ContactAsync(Ct,automatic:true);
        var question=await QuestionAsync(host,contact);await SendNextAsync(host);
        var wrong=await host.ChooseAsync(contact,question.RequestId,host.Forms.Form.DefinitionId,Guid.NewGuid(),Ct);
        var processor=host.Host.Services.GetRequiredService<WhatsAppInboundProcessor>();await processor.ProcessPendingAsync(Ct);
        Assert.Equal((1,0),await CountsAsync(host,contact.ContactId));
        Assert.True(await host.ScopeAsync(services=>services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .AnyAsync(item=>item.Id==wrong && item.AssistantProcessedAtUtc!=null,Ct)));
        await host.ChooseAsync(contact,question.RequestId,host.Forms.Form.DefinitionId,host.Forms.Form.VersionId,Ct);
        await processor.ProcessPendingAsync(Ct);await processor.ProcessPendingAsync(Ct);
        Assert.Equal((2,1),await CountsAsync(host,contact.ContactId));Assert.Empty(host.Operations.SubmitRequests);
    }

    [Fact]
    public async Task An_owner_withdrawal_after_the_question_prevents_the_selected_flow()
    {
        await using var host=await WhatsAppAssistantScenario.CreateAsync(factory,Ct);var contact=await host.ContactAsync(Ct,automatic:true);
        var question=await QuestionAsync(host,contact);await SendNextAsync(host);
        host.Forms.ValuesAvailableForSending=false;
        await host.ChooseAsync(contact,question.RequestId,host.Forms.Form.DefinitionId,host.Forms.Form.VersionId,Ct);
        await host.Host.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);
        Assert.Equal((1,0),await CountsAsync(host,contact.ContactId));Assert.Single(host.Cloud.Messages);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked,await host.ScopeAsync(services=>services.GetRequiredService<ApplicationDbContext>()
            .Set<WhatsAppAssistantRequest>().AsNoTracking().Where(item=>item.RequestId==question.RequestId).Select(item=>item.State).SingleAsync(Ct)));
    }

    private static async Task<AssistantOperationResponse> QuestionAsync(WhatsAppAssistantScenario host,WhatsAppAssistantContact contact)
    {
        var work=await PrepareAsync(host,contact.ContactId);var outcome=host.Outcome(contact,work,"clarification");
        await host.CompleteOutcomeAsync(contact.ContactId,outcome,Ct);return outcome;
    }
    private static async Task<WhatsAppAssistantWorkResponse> PrepareAsync(WhatsAppAssistantScenario host,Guid contactId)
    {
        var work=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantDispatchService>().PrepareAsync(contactId,Ct));
        Assert.True(work.IsSuccess);return Assert.IsType<WhatsAppAssistantWorkResponse>(work.Value);
    }
    private static Task SendNextAsync(WhatsAppAssistantScenario host)=>host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
    private static Task<(int Outgoing,int Sessions)> CountsAsync(WhatsAppAssistantScenario host,Guid contactId)=>host.ScopeAsync(async services=>
    {
        var db=services.GetRequiredService<ApplicationDbContext>();
        return(await db.Set<WhatsAppOutgoingMessage>().CountAsync(item=>item.ContactId==contactId,Ct),
            await db.Set<WhatsAppFlowSession>().CountAsync(item=>item.ContactId==contactId,Ct));
    });
}
