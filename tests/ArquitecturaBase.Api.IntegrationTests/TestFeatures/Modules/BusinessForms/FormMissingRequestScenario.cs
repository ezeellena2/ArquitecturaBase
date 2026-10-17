using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed record FormMissingRequestScenario(ApiFactory Factory,Guid SubjectId,Guid ManagerId,Guid OtherSubjectId,string Culture,string Suffix)
{
    public static async Task<FormMissingRequestScenario> CreateAsync(ApiFactory factory,string culture,CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var actors = await factory.InTransactionAsync(async services =>
        {
            var roleName = "missing-form-manager-" + suffix;
            await services.GetRequiredService<IRoleRepository>().CreateAsync(roleName,null,[BusinessFormsPermissions.Manage],ct);
            var users = services.GetRequiredService<IUserRepository>();
            var subject = await users.CreateAsync(Email.Create(TestEmails.Unique("missing-requester")).Value,null,false,"Private requester " + suffix,culture,ct);
            var other = await users.CreateAsync(Email.Create(TestEmails.Unique("missing-other")).Value,null,false,"Private other " + suffix,culture,ct);
            var manager = await users.CreateAsync(Email.Create(TestEmails.Unique("missing-manager")).Value,null,false,"Missing reviewer " + suffix,culture,ct);
            await users.SetRolesAsync(manager.Id,[roleName],ct);
            return (Subject:subject.Id,Manager:manager.Id,Other:other.Id);
        });
        return new(factory,actors.Subject,actors.Manager,actors.Other,culture,suffix);
    }
    public async Task<StructuredMissingFormRequestCaptureRequest> PrepareAsync(Guid? subject = null,Guid? existing = null,CancellationToken ct = default)
    {
        var actor = subject ?? SubjectId;
        return await Factory.ExecuteScopeAsync(async services =>
        {
            var context = new AssistantChannelContext("whatsapp-assistant",actor,actor,Culture,"America/Argentina/Buenos_Aires",new([],[]),
                [new("forms.assistant-candidates",1),new("forms.missing-request.propose",1)],[new("assistant_proposal",1)]);
            var sources = services.GetServices<IAssistantCapabilitySource>().ToArray(); var turn = Guid.NewGuid();
            var reader = Assert.Single(sources,source => source.Definitions.Any(definition => definition.Id == "forms.assistant-candidates"));
            var read = await reader.ExecuteAsync(new(turn,context,new("forms.assistant-candidates",1),"{}",[]),ct);
            Assert.True(read.IsSuccess); Assert.Empty(Assert.IsType<FormAssistantCandidatesPayload>(read.Value.Payload).Forms);
            var source = Assert.Single(sources,item => item.Definitions.Any(definition => definition.Id == "forms.missing-request.propose"));
            var json = System.Text.Json.JsonSerializer.Serialize(new { existingTopicId = existing,
                name = existing.HasValue ? null : (Culture == "es" ? "Mantenimiento " : "Maintenance ") + Suffix,
                description = existing.HasValue ? null : Culture == "es" ? "Piden registrar tareas de mantenimiento." : "They ask to record maintenance work." });
            var proposal = await source.ExecuteAsync(new(turn,context,new("forms.missing-request.propose",1),json,[new(turn,1,new("forms.assistant-candidates",1),read.Value.Payload)]),ct);
            Assert.True(proposal.IsSuccess);
            var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
            var time = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
            return new StructuredMissingFormRequestCaptureRequest(Guid.NewGuid(),time.AddMinutes(-1),context,
                new(turn,Guid.NewGuid(),new("forms.missing-request.propose",1),proposal.Value.Payload) { ObservedReads = [new(turn,1,new("forms.assistant-candidates",1),read.Value.Payload)] },
                new("fictitious-profile",1,"fictitious-instructions",1,time,context.ChannelId));
        });
    }
    public Task<ArquitecturaBase.Domain.Results.Result<StructuredMissingFormRequestCaptureResponse>> CaptureAsync(StructuredMissingFormRequestCaptureRequest request,CancellationToken ct) =>
        Factory.ExecuteScopeAsync(services => services.GetRequiredService<IStructuredMissingFormRequestSink>().CaptureAsync(request,ct));
    public HttpClient Client(Guid actor)
    {
        var client = Factory.CreateClient(); client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader,actor.ToString()); return client;
    }
}
