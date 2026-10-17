using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed record WhatsAppAssistantCapturedReply(Guid MessageId,Guid SessionId,Guid RecordId,string WaMessageId);

internal static class WhatsAppAssistantCaptureFixture
{
    public static Task<WhatsAppAssistantScenario> CreateAsync(ApiFactory factory,CancellationToken ct)=>
        WhatsAppAssistantScenario.CreateAsync(factory,ct,builder=>builder.ConfigureTestServices(Configure));

    internal static void Configure(IServiceCollection services)
    {
        services.AddSingleton(provider=>new CapturingStructuredRecordPort(((WhatsAppAssistantFormSource)provider.GetRequiredService<IStructuredFormSource>()).Snapshot));
        services.Replace(ServiceDescriptor.Singleton<IStructuredRecordSink>(provider=>provider.GetRequiredService<CapturingStructuredRecordPort>()));
        services.Replace(ServiceDescriptor.Scoped<IStructuredRecordSource>(provider=>provider.GetRequiredService<CapturingStructuredRecordPort>()
            .Source(provider.GetRequiredService<IPermissionService>())));
    }

    public static async Task<WhatsAppFlowMessageResponse> SendManualParentAsync(WhatsAppAssistantScenario host,WhatsAppAssistantContact contact,CancellationToken ct)
    {
        using var client=host.Client(host.FirstOperator);
        using var choices=await client.GetAsync($"/api/whatsapp/chats/{contact.ContactId}/forms",ct);Assert.Equal(HttpStatusCode.OK,choices.StatusCode);
        var revision=(await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
        using var sent=await client.PostAsJsonAsync($"/api/whatsapp/chats/{contact.ContactId}/form-messages",new{
            requestId=Guid.NewGuid(),definitionId=host.Forms.Form.DefinitionId,versionId=host.Forms.Form.VersionId,expectedModeRevision=0,
            expectedRecipientContextRevision=revision,message="Completá la visita."},ct);
        Assert.Equal(HttpStatusCode.Accepted,sent.StatusCode);
        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(ct);
        return Assert.IsType<WhatsAppFlowMessageResponse>(host.Cloud.Messages.Last());
    }

    public static async Task<WhatsAppAssistantCapturedReply> ReplyAsync(WhatsAppAssistantScenario host,WhatsAppAssistantContact contact,
        WhatsAppFlowMessageResponse flow,Guid fieldId,CancellationToken ct,string? waMessageId=null)
    {
        var answer=new JsonObject{["flow_token"]=flow.FlowToken,["field_"+fieldId.ToString("N")]="Sucursal Norte"};
        var wamid=waMessageId??MetaWebhook.UniqueWaMessageId();var waId=flow.To.Value[1..];
        var message=MetaWebhook.Text(wamid,waId,null,host.Clock.GetUtcNow().UtcDateTime,"unused");message.Remove("text");message["type"]="interactive";
        message["interactive"]=new JsonObject{["type"]="nfm_reply",["nfm_reply"]=new JsonObject{
            ["name"]="flow",["body"]="Sent",["response_json"]=answer.ToJsonString()}};
        var payload=MetaWebhook.Build(contacts:[MetaWebhook.Contact(waId,null,"Persona ficticia")],messages:[message]);
        using var request=new HttpRequestMessage(HttpMethod.Post,"/webhooks/whatsapp"){Content=new ByteArrayContent(payload)};
        request.Content.Headers.ContentType=new("application/json");request.Headers.Add("X-Hub-Signature-256",MetaWebhook.Sign(payload));
        using var client=host.Client();using var response=await client.SendAsync(request,ct);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppFlowReplyService>().ProcessPendingAsync(ct));
        var row=await host.ScopeAsync(services=>services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .SingleAsync(item=>item.WaMessageId==wamid,ct));
        Assert.Equal(WhatsAppFlowCaptureState.Completed,row.FlowCaptureState);Assert.NotNull(row.FlowSessionId);Assert.NotNull(row.FlowRecordId);
        Assert.Equal(contact.ContactId,row.ContactId);return new(row.Id,row.FlowSessionId.Value,row.FlowRecordId.Value,wamid);
    }
}
