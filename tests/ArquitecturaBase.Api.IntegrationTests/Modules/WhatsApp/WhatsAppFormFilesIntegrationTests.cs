using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Files;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Files;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Caching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
public sealed partial class WhatsAppFormSendingTests
{
    [Fact]
    public async Task Real_files_flow_is_atomic_private_reloadable_and_independent_of_message_retention()
    {
        await using var emulator = new AzuriteFixture(); await emulator.StartAsync(Ct);
        var store = new AzureBlobFileStore(Options.Create(new FileStorageOptions { Enabled = true, CredentialMode = "ConnectionString", ConnectionString = emulator.ConnectionString }));
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        var transport = new CapturingFormReplyTransport();
        byte[] photo;
        using (var surface = SKSurface.Create(new SKImageInfo(640, 480)))
        {
            surface.Canvas.Clear(SKColors.SeaGreen); using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80); photo = data.ToArray();
        }
        var pdf = "%PDF-1.7\n1 0 obj<<>>endobj\n%%EOF"u8.ToArray();
        var scenario = "Valid";
        using var handler = new StubFlowHttpHandler(request =>
        {
            if (scenario is "Unavailable" or "Expired") return new(scenario == "Expired" ? HttpStatusCode.NotFound : HttpStatusCode.ServiceUnavailable);
            var mediaId = request.RequestUri!.Host == "graph.facebook.com" ? request.RequestUri.Segments.Last() : request.RequestUri.AbsolutePath.Trim('/');
            var bytes = mediaId == "123" && scenario != "Type" ? photo : pdf;
            var type = mediaId == "123" ? "image/jpeg" : "application/pdf";
            var claimedHash = scenario == "Hash" && mediaId == "123" ? new string('0', 64) : Convert.ToBase64String(SHA256.HashData(bytes));
            if (request.RequestUri.Host == "graph.facebook.com") return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = mediaId, mime_type = type, sha256 = claimedHash, file_size = scenario == "Size" ? 26 * 1024 * 1024 : bytes.Length, url = "https://lookaside.fbsbx.com/" + mediaId }) };
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        using var mediaHttp = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        var preview = WhatsAppFormAppPreview.FromEnvironment("es");
        await using var host = FormsHost(provider, new CapturingWhatsAppCloudClient(), null, clock).WithWebHostBuilder(builder => { preview?.Configure(builder); builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPrivateFileStore>(); services.AddSingleton<IPrivateFileStore>(store);
            services.RemoveAll<IWhatsAppCloudClient>(); services.AddSingleton<IWhatsAppCloudClient>(transport);
            services.RemoveAll<IWhatsAppMediaClient>(); services.AddSingleton<IWhatsAppMediaClient>(new WhatsAppMediaClient(mediaHttp, Options.Create(new WhatsAppOptions { PhoneNumberId = ApiFactory.WhatsAppPhoneNumberId, AccessToken = "synthetic-media-token", GraphApiVersion = "v25.0" }), TimeProvider.System));
            services.RemoveAll<IWhatsAppDispatchCoordinator>(); services.AddSingleton<IWhatsAppDispatchCoordinator>(sp => new WhatsAppDispatchCoordinator(sp.GetRequiredService<RedisCache>(), TimeProvider.System));
        }); });
        if (preview is not null) WhatsAppFormAppPreview.Listen(host);
        using var author = await OperatorAsync(host, "formtemplates.read", "formtemplates.manage", "formtemplates.publish", "formrecords.read", "whatsapp.read", "whatsapp.send");
        using var employee = await OperatorAsync(host, "formrecords.create");
        using var denied = await OperatorAsync(host, "whatsapp.read");
        using var reader = await OperatorAsync(host, "formrecords.read");
        var actorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        using var created = await author.PostAsJsonAsync("/api/forms/definitions", new {
            requestId = Guid.NewGuid(), name = new { es = "Visita con archivos", en = "Visit with files" }, prefix = "AF" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            draft = new { format = 1, culture = "es", submitLabel = new { es = "Confirmar visita", en = "Confirm visit" }, fields = new object[] {
                new {localId="photos",type="photo",label=new {es="Fotos de la obra",en="Site photos"},required=true,files=new {minFiles=1,maxFiles=2,photoSource="cameraOrGallery"}},
                new {localId="document",type="document",label=new {es="Remito firmado",en="Signed delivery note"},required=true,files=new {minFiles=1,maxFiles=1}},
            }}}, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.ReadJsonAsync(); var definition = saved.GetProperty("definition");
        var definitionId = definition.GetProperty("id").GetGuid();
        await ExportFilesAsync("draft-created", saved);
        var body = JsonNode.Parse(definition.GetRawText())!.AsObject();
        foreach (var key in new[] { "id", "prefixLocked", "revision" }) body.Remove(key);
        body["expectedRevision"] = definition.GetProperty("revision").GetInt64();
        var fields = body["draft"]!["fields"]!.AsArray();
        var keys = fields.Select(field => field!["key"]!.GetValue<string>()).ToArray();
        for (var i=0;i<fields.Count;i++) { fields[i]!["localId"] = "file-"+i; fields[i]!.AsObject().Remove("key"); }
        using var check = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publication-checks", body, Ct);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode); var checkedBody = await check.ReadJsonAsync();
        Assert.True(checkedBody.GetProperty("canAccept").GetBoolean(),checkedBody.GetRawText());
        body["requestId"] = Guid.NewGuid().ToString(); body["checkToken"] = checkedBody.GetProperty("checkToken").GetString();
        using var accepted = await author.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications",body,Ct);
        Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode); var receipt=await accepted.ReadJsonAsync();
        var versionId=receipt.GetProperty("versionId").GetGuid();var operationId=receipt.GetProperty("operationId").GetGuid();
        var driver=host.Services.GetRequiredService<IStructuredFormPublicationDriver>();
        for(var i=0;i<20;i++){await driver.ProcessAsync(operationId,Ct);await host.Services.GetRequiredService<WhatsAppFlowBackgroundService>().ProcessNextAsync(operationId,Ct);clock.Advance(TimeSpan.FromSeconds(16));}
        using var published=await author.GetAsync(receipt.GetProperty("recoveryPath").GetString(),Ct);
        Assert.Equal("Published",(await published.ReadJsonAsync()).GetProperty("state").GetString());
        Guid? keptRecord=null;Guid? keptFile=null;long expectedNumber=0;
        var contactId=await CreateLinkedContactAsync(employeeId,clock);
        var waId=await factory.ExecuteDbContextAsync(db=>db.Set<WhatsAppContact>().Where(c=>c.Id==contactId).Select(c=>c.WaId).SingleAsync(Ct));
        foreach(var kind in new[]{"Valid","Hash","Type","Size","Unavailable","Expired","ValidAgain"})
        {
            scenario=kind;
            using var choices=await author.GetAsync($"/api/whatsapp/chats/{contactId}/forms",Ct);
            var context=(await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
            using var sent=await author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages",new {requestId=Guid.NewGuid(),definitionId,versionId,expectedModeRevision=0,expectedRecipientContextRevision=context,message="Completá la visita"},Ct);
            Assert.Equal(HttpStatusCode.Accepted,sent.StatusCode);
            var previousCount=transport.Messages.Count;
            for(var i=0;i<5&&transport.Messages.Count==previousCount;i++){await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);if(transport.Messages.Count==previousCount)await Task.Delay(TimeSpan.FromSeconds(1),Ct);}
            Assert.Equal(previousCount+1,transport.Messages.Count);var flow=transport.Messages.Last();
            var answer=new JsonObject{["flow_token"]=flow.FlowToken};
            answer[keys[0]]=new JsonArray(new JsonObject{["id"]="123",["file_name"]="obra.jpg",["mime_type"]="image/jpeg",["sha256"]=kind=="Hash"?new string('0',64):Convert.ToBase64String(SHA256.HashData(kind == "Type" ? pdf : photo))});
            answer[keys[1]]=new JsonArray(new JsonObject{["id"]="124",["file_name"]="remito.pdf",["mime_type"]="application/pdf",["sha256"]=Convert.ToBase64String(SHA256.HashData(pdf))});
            var waMessageId=MetaWebhook.UniqueWaMessageId();
            var message=MetaWebhook.Text(waMessageId,waId,null,clock.GetUtcNow().UtcDateTime,"unused");message.Remove("text");message["type"]="interactive";
            message["interactive"]=new JsonObject{["type"]="nfm_reply",["nfm_reply"]=new JsonObject{["name"]="flow",["body"]="Sent",["response_json"]=answer.ToJsonString()}};
            var payload=MetaWebhook.Build(contacts:[MetaWebhook.Contact(waId,null,"Persona de prueba")],messages:[message]);
            using var request=new HttpRequestMessage(HttpMethod.Post,"/webhooks/whatsapp"){Content=new ByteArrayContent(payload)};
            request.Content.Headers.ContentType=new("application/json");request.Headers.Add("X-Hub-Signature-256",MetaWebhook.Sign(payload));
            using var received=await author.SendAsync(request,Ct);Assert.Equal(HttpStatusCode.OK,received.StatusCode);
            var replyId=await factory.ExecuteDbContextAsync(db=>db.Set<WhatsAppMessage>().Where(m=>m.WaMessageId==waMessageId).Select(m=>m.Id).SingleAsync(Ct));
            var route=$"/api/whatsapp/chats/{contactId}/messages";
            if(kind=="Valid") {using var pending=await author.GetAsync(route,Ct);await ExportFilesAsync("reply-downloading",await pending.ReadJsonAsync());}
            for(var attempt=0;attempt<(kind is "Unavailable" or "Expired"?5:1);attempt++)
            {await using var scope=host.Services.CreateAsyncScope();await scope.ServiceProvider.GetRequiredService<IWhatsAppFlowReplyService>().ProcessPendingAsync(Ct);clock.Advance(TimeSpan.FromMinutes(4));}
            using var response=await author.GetAsync(route,Ct);var thread=await response.ReadJsonAsync();
            var reply=thread.GetProperty("messages").EnumerateArray().Single(m=>m.GetProperty("id").GetGuid()==replyId).GetProperty("formReply");
            Assert.Equal(kind.StartsWith("Valid",StringComparison.Ordinal)?"Completed":"Rejected",reply.GetProperty("state").GetString());
            if(!kind.StartsWith("Valid",StringComparison.Ordinal)){Assert.Equal(JsonValueKind.Null,reply.GetProperty("number").ValueKind);Assert.Equal(JsonValueKind.Null,reply.GetProperty("values").ValueKind);continue;}
            expectedNumber++;
            Assert.EndsWith("-"+expectedNumber.ToString("D6",System.Globalization.CultureInfo.InvariantCulture),reply.GetProperty("number").GetString());
            var recordId=reply.GetProperty("recordId").GetGuid();var values=reply.GetProperty("values");var photoId=values[0].GetProperty("value")[0].GetProperty("fileId").GetGuid();var pdfId=values[1].GetProperty("value")[0].GetProperty("fileId").GetGuid();
            Assert.DoesNotContain("responses/",reply.GetRawText());
            using var original=await reader.GetAsync($"/api/forms/records/{recordId}/files/{photoId}/content",Ct);
            Assert.Equal(HttpStatusCode.OK,original.StatusCode);Assert.Equal(photo,await original.Content.ReadAsByteArrayAsync(Ct));Assert.Null(original.Content.Headers.ContentDisposition);
            Assert.True(original.Headers.CacheControl!.NoStore);Assert.Equal("nosniff",original.Headers.GetValues("X-Content-Type-Options").Single());
            using var thumb=await reader.GetAsync($"/api/forms/records/{recordId}/files/{photoId}/thumbnail",Ct);Assert.Equal(HttpStatusCode.OK,thumb.StatusCode);Assert.Equal("image/jpeg",thumb.Content.Headers.ContentType!.MediaType);
            using var document=await reader.GetAsync($"/api/forms/records/{recordId}/files/{pdfId}/content",Ct);Assert.Equal("attachment",document.Content.Headers.ContentDisposition!.DispositionType);Assert.Equal(pdf,await document.Content.ReadAsByteArrayAsync(Ct));
            using var forbidden=await denied.GetAsync($"/api/forms/records/{recordId}/files/{photoId}/thumbnail",Ct);Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
            using var anonymous=host.CreateClient();using var noSession=await anonymous.GetAsync($"/api/forms/records/{recordId}/files/{photoId}/content",Ct);Assert.Equal(HttpStatusCode.Unauthorized,noSession.StatusCode);
            using var wrongRecord=await reader.GetAsync($"/api/forms/records/{Guid.NewGuid()}/files/{photoId}/content",Ct);Assert.Equal(HttpStatusCode.NotFound,wrongRecord.StatusCode);
            using var privateResponse=await denied.GetAsync(route,Ct);var privateThread=await privateResponse.ReadJsonAsync();Assert.DoesNotContain("obra.jpg",privateThread.GetRawText());Assert.DoesNotContain("remito.pdf",privateThread.GetRawText());
            if(kind=="Valid") {await ExportFilesAsync("reply-completed",thread);await ExportFilesAsync("reply-without-read",privateThread);using var reload=await author.GetAsync(route,Ct);await ExportFilesAsync("reply-reloaded",await reload.ReadJsonAsync());keptRecord=recordId;keptFile=photoId;}
        }
        await using(var orphan=new MemoryStream([1,2,3]))await store.WriteAsync("orphan-test",orphan,"application/octet-stream",Ct);
        if (preview is not null) await preview.HoldAsync(factory,host,[("files-read",actorId,contactId,"es")],clock,new ControlledWhatsAppSendClient(),Ct);
        clock.Advance(TimeSpan.FromDays(91));
        await host.Services.GetRequiredService<WhatsAppMessageRetentionService>().ClearExpiredTextsAsync(Ct);
        await using(var scope=host.Services.CreateAsyncScope())Assert.True((await scope.ServiceProvider.GetRequiredService<IPrivateFileCleanupService>().PurgeAsync(Ct)).IsSuccess);
        await Assert.ThrowsAsync<Azure.RequestFailedException>(()=>store.OpenReadAsync("orphan-test",Ct));
        using var retained=await reader.GetAsync($"/api/forms/records/{keptRecord}/files/{keptFile}/content",Ct);Assert.Equal(HttpStatusCode.OK,retained.StatusCode);Assert.Equal(photo,await retained.Content.ReadAsByteArrayAsync(Ct));
        static async Task ExportFilesAsync(string name,JsonElement body)
        {
            var output=Environment.GetEnvironmentVariable("FORM_FILES_HTTP_OUTPUT");if(string.IsNullOrWhiteSpace(output))return;Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output,name+".json"),body.GetRawText(),TestContext.Current.CancellationToken);
        }
    }
}
