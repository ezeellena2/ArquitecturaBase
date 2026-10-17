using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantDeclineIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    [Fact]
    public async Task Declining_a_real_question_option_is_durable_and_prevents_a_later_offer_without_treating_it_as_missing()
    {
        await using var host=await WhatsAppAssistantScenario.CreateAsync(factory,Ct);var contact=await host.ContactAsync(Ct,automatic:true);
        var initial=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantDispatchService>().PrepareAsync(contact.ContactId,Ct));
        Assert.True(initial.IsSuccess);Assert.NotNull(initial.Value);
        var question=host.Outcome(contact,initial.Value,"clarification");await host.CompleteOutcomeAsync(contact.ContactId,question,Ct);
        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        contact=contact with {SourceMessageId=await host.AddNewerSourceAsync(contact,Ct)};
        var next=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantDispatchService>().PrepareAsync(contact.ContactId,Ct));
        Assert.True(next.IsSuccess);Assert.NotNull(next.Value);var work=next.Value;
        var operation=Assert.IsType<AssistantOperationRequest>(work.Operation);
        var turn=Guid.NewGuid();var readCapability=new AssistantCapabilityReference("whatsapp.offered-forms",1);
        var declineCapability=new AssistantCapabilityReference("whatsapp.decline-form",1);
        var proposal=await host.ScopeAsync(async services=>
        {
            var source=Assert.Single(services.GetServices<IAssistantCapabilitySource>(),source=>source.Definitions.Any(item=>item.Id==readCapability.Id));
            var read=await source.ExecuteAsync(new(turn,operation.Context,readCapability,"{}",[]),Ct);Assert.True(read.IsSuccess);
            var offers=Assert.IsType<StructuredFormOffersPayload>(read.Value.Payload);
            Assert.Contains(offers.Offers,offer=>offer.RequestId==question.RequestId && offer.DefinitionId==host.Forms.Form.DefinitionId);
            var observed=new AssistantObservedRead(turn,1,readCapability,read.Value.Payload);
            var arguments=JsonSerializer.Serialize(new{requestId=question.RequestId,definitionId=host.Forms.Form.DefinitionId},JsonSerializerOptions.Web);
            var declined=await source.ExecuteAsync(new(turn,operation.Context,declineCapability,arguments,[observed]),Ct);Assert.True(declined.IsSuccess);
            return new AssistantObservedProposal(turn,Guid.NewGuid(),declineCapability,declined.Value.Payload){ObservedReads=[observed]};
        });
        var outcome=new AssistantOperationResponse(work.RequestId,"completed",proposal,
            new("http-fixture",1,"http-fixture",1,operation.OccurredAtUtc,operation.Context.ChannelId),null);
        host.Operations.Complete(outcome);await host.CompleteOutcomeAsync(contact.ContactId,outcome,Ct);await host.CompleteOutcomeAsync(contact.ContactId,outcome,Ct);
        var declinedRows=await host.ScopeAsync(services=>services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .Where(row=>row.RequestId==work.RequestId).Select(row=>row.DeclinedDefinitionId).ToListAsync(Ct));
        Assert.Equal(host.Forms.Form.DefinitionId,Assert.Single(declinedRows));
        var history=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().ListOffersAsync(contact.RecipientId,Ct));
        Assert.True(Assert.Single(history,row=>row.RequestId==question.RequestId && row.DefinitionId==host.Forms.Form.DefinitionId).Declined);
        Assert.False(Assert.Single(history,row=>row.RequestId==question.RequestId && row.DefinitionId==host.Forms.AlternativeForm.DefinitionId).Declined);
        contact=contact with {SourceMessageId=await host.AddNewerSourceAsync(contact,Ct)};
        var later=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantDispatchService>().PrepareAsync(contact.ContactId,Ct));Assert.NotNull(later.Value);
        await host.CompleteOutcomeAsync(contact.ContactId,host.Outcome(contact,later.Value,"ready"),Ct);
        var count=await host.ScopeAsync(services=>services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().CountAsync(row=>row.ContactId==contact.ContactId,Ct));
        Assert.Equal(1,count);Assert.Single(host.Cloud.Messages);
    }
}
