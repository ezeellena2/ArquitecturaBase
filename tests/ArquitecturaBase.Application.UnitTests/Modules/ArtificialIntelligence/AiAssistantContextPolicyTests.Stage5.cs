using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;
public sealed partial class AiAssistantContextPolicyTests
{
    private static readonly string[] Stage5Capabilities=["whatsapp.offered-forms","whatsapp.no-reply","whatsapp.decline-form","forms.assistant-candidates","forms.propose-registration","forms.clarify-registration","forms.suggestion-candidates","forms.propose-suggestion","forms.catalog-filters.metadata","forms.catalog-filters.propose","forms.missing-request.propose"];
    [Fact]
    public async Task Actual_channel_acceptance_and_claim_prepare_eleven_capabilities_plus_one_terminal_without_Home()
    {
        var ct=TestContext.Current.CancellationToken;var fixture=new AssistantOperationsFixture();var limits=new AiAssistantLimits();
        fixture.Permissions.Permissions[fixture.ActorId]=["whatsapp.read","whatsapp.send"];
        var source=new PreparedAssistantCapabilitySource(Stage5Capabilities.Select(id=>PreparedAssistantCapabilitySource.Definition(id,kind:id is "whatsapp.offered-forms" or "forms.assistant-candidates" or "forms.suggestion-candidates" or "forms.catalog-filters.metadata" ? AssistantCapabilityKind.Read : AssistantCapabilityKind.Proposal,permissions:["whatsapp.read"])).ToArray());
        var catalog=new AiAssistantCapabilityCatalogPolicy([source],fixture.Permissions);
        var guard=new AiAssistantAccessGuard(fixture.Users,fixture.Permissions,fixture.Profiles,limits,new FakeCurrentUser{UserId=fixture.ActorId},catalog,[]);
        var responses=new AiAssistantResponsePolicy(fixture.Protector,fixture.Clock,catalog);
        var submissions=new AiAssistantSubmissionPolicy(fixture.Store,fixture.Store,fixture.Store,guard,fixture.Protector,new OperationsFingerprint(),limits,fixture.Clock);
        var service=new AiAssistantOperations(fixture.Store,guard,submissions,fixture.Work,fixture.Store,responses,NullLogger<AiAssistantOperations>.Instance);
        var context=fixture.Context with{ChannelId="whatsapp-manual",RequiredPermissions=new(["whatsapp.read","whatsapp.send"],["formrecords.create"]),
            AllowedCapabilities=Stage5Capabilities.Select(id=>new AssistantCapabilityReference(id,1)).ToArray()};
        var accepted=await service.SubmitAsync(fixture.Request() with{Context=context},ct);Assert.True(accepted.IsSuccess);
        var dispatch=new AiAssistantDispatchService(fixture.Store,fixture.Work,new(fixture.Store,fixture.Store,fixture.Store,guard,catalog),fixture.Protector,limits,fixture.Clock,NullLogger<AiAssistantDispatchService>.Instance, new DisabledAssistantOperationCompletionSignal());
        var claimed=await dispatch.ClaimAsync(ct);Assert.True(claimed.IsSuccess);Assert.NotNull(claimed.Value);
        var prepared=await new AiAssistantContextPolicy(fixture.Store,fixture.Store,guard,responses,new AiConnectionCandidatePolicy(new SecretProtector()),catalog,limits,fixture.Clock).PrepareAsync(claimed.Value,ct);
        Assert.True(prepared.IsSuccess);Assert.Equal(12,prepared.Value.ProviderRequest.Tools.Count);
        Assert.Equal(Stage5Capabilities.Append("assistant_proposal"),prepared.Value.ProviderRequest.Tools.Select(tool=>tool.Name));
        Assert.DoesNotContain("users.summary",prepared.Value.ProviderRequest.Tools.Select(tool=>tool.Name));
        Assert.DoesNotContain("ai.assistant.use",fixture.Permissions.Permissions[fixture.ActorId]);Assert.Equal(0,source.Calls);
        Assert.Equal(6,limits.ToolCalls);Assert.Equal(4,limits.ProviderSteps);
    }
}
