using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantIsolationIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Three_chats_keep_their_own_proposals_contexts_and_destinations_while_one_provider_call_is_still_running(bool automatic)
    {
        string[] markers=["Taller Aster","Taller Brisa","Taller Cauce"];
        await using var scenario=await WhatsAppAssistantIsolationScenario.CreateAsync(factory,markers);
        var contacts=new List<AssistantIsolationContact>();
        foreach(var marker in markers)contacts.Add(await scenario.ContactAsync(automatic,marker));
        try
        {
            var first=await scenario.StartAsync(contacts[0],automatic);
            await scenario.Probe.WaitForStartedAsync(contacts[0].Marker,Ct);
            var others=await Task.WhenAll(contacts.Skip(1).Select(contact=>scenario.StartAsync(contact,automatic)));
            await Task.WhenAll(others.Select(request=>scenario.Probe.WaitForCompletedAsync(request.Contact.Marker,Ct)));

            foreach(var request in others)await ReviewAndSendAsync(request);
            await Task.WhenAll(others.Select(request=>scenario.WaitForSendAsync(request.Contact)));
            Assert.False(scenario.Probe.IsReleased);
            var held=await scenario.ReferenceAsync(first);
            Assert.Equal(WhatsAppAssistantRequestState.Pending,held.State);
            Assert.Null(held.OutgoingId);
            Assert.DoesNotContain(scenario.Cloud.Sent,item=>item.Message.To.Value[1..]==first.Contact.WaId);
            Assert.Equal(2,scenario.Cloud.Sent.Count);

            scenario.Probe.Release();
            await scenario.Probe.WaitForCompletedAsync(first.Contact.Marker,Ct);
            await ReviewAndSendAsync(first);
            await scenario.WaitForSendAsync(first.Contact);
            AssistantIsolationRequest[] requests=[first,..others];
            var references=new List<WhatsAppAssistantRequest>();
            var contexts=new Dictionary<Guid,AssistantChannelContext>();
            foreach(var request in requests)
            {
                var reference=await scenario.ReferenceAsync(request);
                references.Add(reference);
                Assert.Equal(request.Contact.ContactId,reference.ContactId);
                Assert.Equal(request.SourceMessageId,reference.SourceMessageId);
                Assert.Equal(request.Contact.UserId,reference.SubjectUserId);
                Assert.Equal(WhatsAppAssistantRequestState.Sent,reference.State);
                Assert.Equal(automatic?WhatsAppAssistantRequestKind.Automatic:WhatsAppAssistantRequestKind.Manual,reference.Kind);
                var context=WhatsAppAssistantIsolationScenario.Context(reference);
                contexts.Add(request.RequestId,context);
                Assert.Equal(automatic?request.Contact.UserId:scenario.ActorUserId,context.ActorUserId);
                Assert.Equal(request.Contact.UserId,context.SubjectUserId);
                Assert.Equal(automatic?AssistantInteractionMode.Assistant:AssistantInteractionMode.Manual,context.InteractionMode);
                if(!automatic)
                {
                    using var captured=JsonDocument.Parse(reference.ContextJson!);
                    Assert.Equal(request.Contact.ContactId,captured.RootElement.GetProperty("channelConversationId").GetGuid());
                }
                var own=await scenario.OperationAsync(context,request.RequestId);
                Assert.True(own.IsSuccess);
                Assert.Equal("completed",own.Value.State);
                Assert.NotNull(own.Value.Proposal);
                Assert.Equal(reference.ProposalId,own.Value.Proposal.Id);

                var sent=Assert.Single(scenario.Cloud.Sent,item=>item.Message.To.Value[1..]==request.Contact.WaId);
                var flow=Assert.IsType<WhatsAppFlowMessageResponse>(sent.Message);
                Assert.Equal("+"+request.Contact.WaId,flow.To.Value);
                Assert.Equal(request.Contact.Marker,Assert.Single(flow.InitialValues.SelectMany(screen=>screen.Value)).Value);
                Assert.Equal(3,scenario.Probe.Calls(request.Contact.Marker));
            }

            for(var index=0;index<requests.Length;index++)
            {
                var request=requests[index];
                var another=requests[(index+1)%requests.Length];
                var wrong=await scenario.OperationAsync(contexts[another.RequestId],request.RequestId);
                Assert.True(wrong.IsFailure);
                Assert.Equal(ErrorType.NotFound,wrong.Error.Type);
                if(!automatic)
                {
                    using var wrongChat=await scenario.Client.GetAsync(WhatsAppAssistantIsolationScenario.Route(another.Contact.ContactId)+"/"+request.RequestId,Ct);
                    Assert.Equal(HttpStatusCode.NotFound,wrongChat.StatusCode);
                }
            }

            Assert.Equal(3,references.Select(row=>row.RequestId).Distinct().Count());
            Assert.Equal(3,references.Select(row=>row.ProposalId).Distinct().Count());
            Assert.Equal(3,references.Select(row=>row.OutgoingId).Distinct().Count());
            Assert.Equal(3,scenario.Cloud.Sent.Count);
            var sessions=await scenario.ReadAsync(db=>db.Set<WhatsAppFlowSession>().AsNoTracking().ToListAsync(Ct));
            Assert.Equal(3,sessions.Count);
            foreach(var request in requests)
            {
                var session=Assert.Single(sessions,row=>row.ContactId==request.Contact.ContactId);
                Assert.Equal(request.Contact.UserId,session.UserId);
                Assert.Equal(scenario.DefinitionId,session.DefinitionId);
                Assert.Equal(scenario.VersionId,session.VersionId);
            }

            async Task ReviewAndSendAsync(AssistantIsolationRequest request)
            {
                if(automatic)return;
                var proposal=await scenario.WaitForProposalAsync(request);
                Assert.Equal(scenario.DefinitionId,proposal.GetProperty("definitionId").GetGuid());
                Assert.Equal(scenario.VersionId,proposal.GetProperty("versionId").GetGuid());
                var field=Assert.Single(proposal.GetProperty("fields").EnumerateArray());
                Assert.Equal(request.Contact.Marker,field.GetProperty("displayValue").GetString());
                await scenario.SendProposalAsync(request,field.GetProperty("fieldId").GetGuid());
            }
        }
        finally {scenario.Probe.Release();}
    }
}