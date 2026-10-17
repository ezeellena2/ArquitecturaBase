using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;
public sealed partial class AiAssistantProviderContractTests
{
    private static readonly string[] Stage5Capabilities=["whatsapp.offered-forms","whatsapp.no-reply","whatsapp.decline-form","forms.assistant-candidates","forms.propose-registration","forms.clarify-registration","forms.suggestion-candidates","forms.propose-suggestion","forms.catalog-filters.metadata","forms.catalog-filters.propose","forms.missing-request.propose"];
    private static LlmConversationRequest Stage5Request(AiProvider provider)=>Request(provider) with
        {Tools=Stage5Capabilities.Select(id=>new LlmToolDefinition(id,"A current channel capability.",EmptySchema)).Append(new("assistant_proposal","Select an observed proposal.",EmptySchema)).ToArray()};
    private static string Stage5Response(AiProvider provider,int count=1)=>provider==AiProvider.OpenAI
        ?JsonSerializer.Serialize(new{status="completed",output=Enumerable.Range(1,count).Select(index=>new{type="function_call",call_id="call-"+index,name="whatsapp_no-reply",arguments="{}"})})
        :JsonSerializer.Serialize(new{type="message",stop_reason="tool_use",content=Enumerable.Range(1,count).Select(index=>new{type="tool_use",id="call-"+index,name="whatsapp_no-reply",input=new{} })});
    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task A_real_eleven_capability_channel_sends_twelve_definitions_with_the_same_execution_budgets(AiProvider provider)
    {
        using var handler=new RecordingHandler(Stage5Response(provider));using var http=new HttpClient(handler);var limits=new AiAssistantLimits();
        var result=await Client(provider,http,limits:limits).SendAsync(Stage5Request(provider),Ct);
        Assert.True(result.IsSuccess);Assert.Equal("whatsapp.no-reply",Assert.Single(result.Value.ToolCalls).Name);
        using var body=JsonDocument.Parse(Assert.Single(handler.Bodies));Assert.Equal(12,body.RootElement.GetProperty("tools").GetArrayLength());
        Assert.Equal(Stage5Capabilities.Append("assistant_proposal").Select(name=>name.Replace('.','_')),body.RootElement.GetProperty("tools").EnumerateArray().Select(tool=>tool.GetProperty("name").GetString()));
        Assert.Equal(6,limits.ToolCalls);Assert.Equal(4,limits.ProviderSteps);
    }
    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task More_than_twelve_definitions_is_rejected_before_any_provider_call(AiProvider provider)
    {
        using var handler=new RecordingHandler(Stage5Response(provider));using var http=new HttpClient(handler);
        var request=Stage5Request(provider);var result=await Client(provider,http).SendAsync(request with{Tools=request.Tools.Append(new("fixture.extra","Not in the channel contract.",EmptySchema)).ToArray()},Ct);
        Assert.True(result.IsFailure);Assert.Equal("Ai.Assistant.Invalid",result.Error.Code);Assert.Empty(handler.Bodies);
    }
    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task A_larger_catalog_does_not_raise_the_seven_call_protocol_response_bound(AiProvider provider)
    {
        using var handler=new RecordingHandler(Stage5Response(provider,8));using var http=new HttpClient(handler);
        var result=await Client(provider,http).SendAsync(Stage5Request(provider),Ct);
        Assert.True(result.IsFailure);Assert.Equal("Ai.Assistant.InvalidOutput",result.Error.Code);Assert.Single(handler.Bodies);
    }
}
