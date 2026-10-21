using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Caching;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed record AssistantIsolationContact(Guid UserId,Guid ContactId,string WaId,string Marker);
internal sealed record AssistantIsolationRequest(AssistantIsolationContact Contact,Guid SourceMessageId,Guid RequestId);

internal sealed class WhatsAppAssistantIsolationScenario:IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> setup;
    private readonly WebApplicationFactory<Program> host;
    private HttpClient? client;
    public HttpClient Client=>client??throw new InvalidOperationException("The isolation host has not started.");
    public WhatsAppAssistantLatencyCloud Cloud {get;}=new();
    public WhatsAppAssistantIsolationProbe Probe {get;}
    public Guid ActorUserId {get;private set;}
    public Guid DefinitionId {get;private set;}
    public Guid VersionId {get;private set;}
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;

    private WhatsAppAssistantIsolationScenario(ApiFactory factory,IReadOnlyList<string> markers)
    {
        Probe=new(markers);
        var clock=new FakeTimeProvider(TimeProvider.System.GetUtcNow().AddHours(-1));
        var flow=new CapturingFlowProvider(clock);
        var connection=factory.NewDatabaseConnectionString("assistant_isolation");
        setup=factory.WithWebHostBuilder(builder=>builder
            .UseSetting("ConnectionStrings:appdb",connection).UseSetting("WhatsApp:WabaId","222")
            .ConfigureTestServices(services=>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                services.Replace(ServiceDescriptor.Singleton<IWhatsAppFlowProvider>(flow));
            }));
        host=setup.WithWebHostBuilder(builder=>builder
            .UseSetting("ArtificialIntelligence:Assistant:ProcessInBackground","true")
            .UseSetting("ArtificialIntelligence:Assistant:Limits:RunningCapacity","3")
            .UseSetting("WhatsApp:ProcessInboundInBackground","true")
            .UseSetting("WhatsApp:ProcessOutgoingInBackground","true")
            .UseSetting("WhatsApp:ProcessFlowsInBackground","true")
            .ConfigureTestServices(services=>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider.System));
                services.PostConfigure<WhatsAppOptions>(options=>options.ProcessFlowsInBackground=true);
                services.Replace(ServiceDescriptor.Singleton<IWhatsAppCloudClient>(Cloud));
                services.Replace(ServiceDescriptor.Singleton<IWhatsAppDispatchCoordinator>(provider=>
                    new WhatsAppDispatchCoordinator(provider.GetRequiredService<RedisCache>(),TimeProvider.System)));
                services.AddHttpClient("AiAssistantOpenAI").ConfigurePrimaryHttpMessageHandler(()=>new WhatsAppAssistantIsolationHttpHandler(Probe));
            }));
    }

    public static async Task<WhatsAppAssistantIsolationScenario> CreateAsync(ApiFactory factory,IReadOnlyList<string> markers)
    {
        var scenario=new WhatsAppAssistantIsolationScenario(factory,markers);
        try
        {
            using var author=scenario.setup.CreateClient();
            await ScopeAsync(scenario.setup,services=>services.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync(Ct));
            await scenario.setup.Services.SeedDatabaseAsync(Ct);
            scenario.ActorUserId=await ScopeAsync(scenario.setup,services=>WriteAsync(services,async token=>
            {
                var role="isolation-operator-"+Guid.NewGuid().ToString("N");
                await services.GetRequiredService<IRoleRepository>().CreateAsync(role,null,
                    ["whatsapp.read","whatsapp.send","formtemplates.read","formtemplates.manage","formtemplates.publish",
                     "formrecords.read","formrecords.create","ai.read","ai.manage"],token);
                var users=services.GetRequiredService<IUserRepository>();
                var user=await users.CreateAsync(Email.Create(role+"@example.test").Value,null,false,"Operador ficticio","es",token);
                await users.SetRolesAsync(user.Id,[role],token);
                return user.Id;
            }));
            author.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader,scenario.ActorUserId.ToString());
            using(var current=await author.GetAsync("/api/ai/connection",Ct))
            {
                var revision=(await current.ReadJsonAsync()).GetProperty("revision").GetInt64();
                using var configured=await author.PutAsJsonAsync("/api/ai/connection",new
                {
                    requestId=Guid.NewGuid(),provider="OpenAI",model="fictional-model",credentialChange="Replace",
                    apiKey="fictional-isolation-key",enabled=true,expectedRevision=revision,
                },Ct);
                Assert.Equal(HttpStatusCode.OK,configured.StatusCode);
            }
            await scenario.PublishAsync(author);
            scenario.client=scenario.host.CreateClient();
            scenario.client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader,scenario.ActorUserId.ToString());
            return scenario;
        }
        catch {await scenario.DisposeAsync();throw;}
    }

    public Task<AssistantIsolationContact> ContactAsync(bool automatic,string marker)=>ScopeAsync(host,services=>WriteAsync(services,async token=>
    {
        var phone=PhoneNumber.Create("+"+MetaWebhook.UniqueWaId()).Value;
        var users=services.GetRequiredService<IUserRepository>();
        var user=await users.CreateAsync(null,phone,true,"Persona ficticia","es",token);
        var role="isolation-recipient-"+Guid.NewGuid().ToString("N");
        await services.GetRequiredService<IRoleRepository>().CreateAsync(role,null,["formrecords.create"],token);
        await users.SetRolesAsync(user.Id,[role],token);
        var contact=WhatsAppContact.Create(phone.Value[1..],MetaWebhook.UniqueBsuid(),"Persona ficticia",TimeProvider.System.GetUtcNow().UtcDateTime);
        contact.LinkUser(user.Id);
        if(automatic)contact.ChangeMode(WhatsAppConversationMode.Assistant);
        services.GetRequiredService<ApplicationDbContext>().Add(contact);
        return new AssistantIsolationContact(user.Id,contact.Id,phone.Value[1..],marker);
    }));

    public async Task<AssistantIsolationRequest> StartAsync(AssistantIsolationContact contact,bool automatic)
    {
        var wamid=MetaWebhook.UniqueWaMessageId();
        var message=MetaWebhook.Text(wamid,contact.WaId,null,TimeProvider.System.GetUtcNow().UtcDateTime,"Visité "+contact.Marker+".");
        var body=MetaWebhook.Build(contacts:[MetaWebhook.Contact(contact.WaId,null,"Persona ficticia")],messages:[message]);
        using(var request=new HttpRequestMessage(HttpMethod.Post,"/webhooks/whatsapp"){Content=new ByteArrayContent(body)})
        {
            request.Content.Headers.ContentType=new("application/json");
            request.Headers.Add("X-Hub-Signature-256",MetaWebhook.Sign(body));
            using var response=await Client.SendAsync(request,Ct);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        }
        await WaitAsync(()=>ReadAsync(db=>db.Set<WhatsAppMessage>().AnyAsync(row=>row.WaMessageId==wamid && row.ProcessedAtUtc!=null,Ct)));
        var source=await ReadAsync(db=>db.Set<WhatsAppMessage>().AsNoTracking().SingleAsync(row=>row.WaMessageId==wamid,Ct));
        if(automatic)
        {
            await WaitAsync(()=>ReadAsync(db=>db.Set<WhatsAppAssistantRequest>().AnyAsync(row=>row.ContactId==contact.ContactId && row.SourceMessageId==source.Id,Ct)));
            var reference=await ReadAsync(db=>db.Set<WhatsAppAssistantRequest>().AsNoTracking().SingleAsync(row=>row.ContactId==contact.ContactId && row.SourceMessageId==source.Id,Ct));
            return new(contact,source.Id,reference.RequestId);
        }
        var requestId=Guid.NewGuid();
        using var accepted=await Client.PostAsJsonAsync(Route(contact.ContactId),new{requestId,sourceMessageId=source.Id,expectedModeRevision=0},Ct);
        Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
        return new(contact,source.Id,requestId);
    }

    public async Task<JsonElement> WaitForProposalAsync(AssistantIsolationRequest request)
    {
        JsonElement proposal=default;
        await WaitAsync(async()=>
        {
            using var response=await Client.GetAsync(Route(request.Contact.ContactId)+"/"+request.RequestId,Ct);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var json=await response.ReadJsonAsync();
            if(json.GetProperty("state").GetString()!="Ready")return false;
            proposal=json.GetProperty("proposal").Clone();
            return true;
        });
        return proposal;
    }

    public async Task SendProposalAsync(AssistantIsolationRequest request,Guid fieldId)
    {
        using var response=await Client.PostAsJsonAsync(Route(request.Contact.ContactId)+"/"+request.RequestId+"/send",
            new{requestId=Guid.NewGuid(),includedFieldIds=new[]{fieldId}},Ct);
        Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
    }

    public Task WaitForSendAsync(AssistantIsolationContact contact)=>WaitAsync(()=>Task.FromResult(Cloud.Sent.Any(item=>item.Message.To.Value[1..]==contact.WaId)));
    public Task<WhatsAppAssistantRequest> ReferenceAsync(AssistantIsolationRequest request)=>ReadAsync(db=>db.Set<WhatsAppAssistantRequest>().AsNoTracking()
        .SingleAsync(row=>row.ContactId==request.Contact.ContactId && row.RequestId==request.RequestId,Ct));
    public Task<T> ReadAsync<T>(Func<ApplicationDbContext,Task<T>> read)=>ScopeAsync(host,services=>read(services.GetRequiredService<ApplicationDbContext>()));
    public Task<Result<AssistantOperationResponse>> OperationAsync(AssistantChannelContext context,Guid requestId)=>
        ScopeAsync(host,services=>services.GetRequiredService<IAssistantOperations>().GetAsync(context,requestId,Ct));
    public static AssistantChannelContext Context(WhatsAppAssistantRequest reference)=>
        JsonSerializer.Deserialize<AssistantChannelContext>(reference.ContextJson!,JsonSerializerOptions.Web)
            ??throw new InvalidOperationException("The real channel reference has no captured context.");
    public static string Route(Guid contactId)=>"/api/whatsapp/chats/"+contactId+"/assistant-suggestions";
    private static Task WaitAsync(Func<Task<bool>> condition)=>WhatsAppAssistantLatencyScenario.WaitAsync(condition);

    private async Task PublishAsync(HttpClient author)
    {
        using var created=await author.PostAsJsonAsync("/api/forms/definitions",new
        {
            requestId=Guid.NewGuid(),name=new{es="Visita de aislamiento"},prefix="I"+Guid.NewGuid().ToString("N")[..8],
            offerWhen="Cuando la persona indique que visitó un taller.",
            draft=new{format=1,culture="es",submitLabel=new{es="Enviar"},fields=new[]
            {
                new{localId="location",type="shortText",label=new{es="Lugar"},required=true,maxLength=120},
            }},
        },Ct);
        Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        var definition=(await created.ReadJsonAsync()).GetProperty("definition");
        DefinitionId=definition.GetProperty("id").GetGuid();
        var attempt=JsonNode.Parse(definition.GetRawText())!.AsObject();
        foreach(var property in new[]{"id","prefixLocked","revision","offerWhen"})attempt.Remove(property);
        attempt["expectedRevision"]=definition.GetProperty("revision").GetInt64();
        var field=attempt["draft"]!["fields"]![0]!.AsObject();
        field["localId"]="location";field.Remove("key");
        using var check=await author.PostAsJsonAsync("/api/forms/definitions/"+DefinitionId+"/publication-checks",attempt,Ct);
        Assert.Equal(HttpStatusCode.OK,check.StatusCode);
        attempt["requestId"]=Guid.NewGuid().ToString();
        attempt["checkToken"]=(await check.ReadJsonAsync()).GetProperty("checkToken").GetString();
        using var accepted=await author.PostAsJsonAsync("/api/forms/definitions/"+DefinitionId+"/publications",attempt,Ct);
        Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
        var receipt=await accepted.ReadJsonAsync();
        VersionId=receipt.GetProperty("versionId").GetGuid();
        var operationId=receipt.GetProperty("operationId").GetGuid();
        var clock=(FakeTimeProvider)setup.Services.GetRequiredService<TimeProvider>();
        for(var step=0;step<20;step++)
        {
            await setup.Services.GetRequiredService<IStructuredFormPublicationDriver>().ProcessAsync(operationId,Ct);
            await setup.Services.GetRequiredService<WhatsAppFlowBackgroundService>().ProcessNextAsync(operationId,Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
            using var status=await author.GetAsync(receipt.GetProperty("recoveryPath").GetString(),Ct);
            if((await status.ReadJsonAsync()).GetProperty("state").GetString()=="Published")return;
        }
        Assert.Fail("The real publication workers must complete isolation setup.");
    }

    private static async Task<T> ScopeAsync<T>(WebApplicationFactory<Program> factory,Func<IServiceProvider,Task<T>> action)
    {
        await using var scope=factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }
    private static async Task<T> WriteAsync<T>(IServiceProvider services,Func<CancellationToken,Task<T>> action)
    {
        T value=default!;
        var result=await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct=>
        {value=await action(ct);return Result.Success();},CommitPolicy.OnSuccess,Ct);
        Assert.True(result.IsSuccess);
        return value;
    }
    public async ValueTask DisposeAsync()
    {
        Probe.Release();
        client?.Dispose();
        await host.DisposeAsync();
        try
        {
            await using var scope=setup.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync(CancellationToken.None);
        }
        finally {await setup.DisposeAsync();}
    }
}