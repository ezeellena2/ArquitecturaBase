using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

// Controlled provider protocol only; the application still creates and validates every proposal.
internal sealed class WhatsAppAssistantIsolationProbe(IReadOnlyList<string> markers)
{
    private readonly TaskCompletionSource<bool> release=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<string,TaskCompletionSource<bool>> entered=new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string,TaskCompletionSource<bool>> completed=new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string,int> calls=new(StringComparer.Ordinal);
    private int ordinal;

    public IReadOnlyList<string> Markers=>markers;
    public bool IsReleased=>release.Task.IsCompleted;
    public int Calls(string marker)=>calls.GetValueOrDefault(marker);
    public Task WaitForStartedAsync(string marker,CancellationToken ct)=>Signal(entered,marker).Task.WaitAsync(TimeSpan.FromSeconds(30),ct);
    public Task WaitForCompletedAsync(string marker,CancellationToken ct)=>Signal(completed,marker).Task.WaitAsync(TimeSpan.FromSeconds(30),ct);
    public void Release()=>release.TrySetResult(true);
    public int Start(string marker)=>calls.AddOrUpdate(marker,1,static (_,count)=>count+1);
    public string NextCallId()=>"isolation-"+Interlocked.Increment(ref ordinal).ToString(System.Globalization.CultureInfo.InvariantCulture);
    public void Complete(string marker)=>Signal(completed,marker).TrySetResult(true);
    public async Task EnterAsync(string marker,CancellationToken ct)
    {
        Signal(entered,marker).TrySetResult(true);
        if(marker==markers[0])await release.Task.WaitAsync(ct);
    }
    private static TaskCompletionSource<bool> Signal(ConcurrentDictionary<string,TaskCompletionSource<bool>> signals,string marker)=>
        signals.GetOrAdd(marker,static _=>new(TaskCreationOptions.RunContinuationsAsynchronously));
}

internal sealed class WhatsAppAssistantIsolationHttpHandler(WhatsAppAssistantIsolationProbe probe):HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        Require(request.Method==HttpMethod.Post && request.RequestUri is
            {Scheme:"https",Host:"api.openai.com",IsDefaultPort:true,AbsolutePath:"/v1/responses"},"Unexpected isolation provider endpoint.");
        Require(request.Content is not null,"Missing isolation provider request.");
        using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var root=body.RootElement;
        Require(!root.GetProperty("store").GetBoolean() && !root.GetProperty("parallel_tool_calls").GetBoolean(),
            "Expected private serial tool execution.");
        var input=root.GetProperty("input");
        Require(input.ValueKind==JsonValueKind.Array,"Missing isolation conversation input.");
        var userText=string.Join("\n",input.EnumerateArray().Where(item=>Text(item,"role")=="user")
            .Select(item=>RequiredText(item,"content")));
        var marker=probe.Markers.Single(value=>userText.Contains(value,StringComparison.Ordinal));
        var wholeInput=input.GetRawText();
        Require(probe.Markers.Where(value=>wholeInput.Contains(value,StringComparison.Ordinal)).SequenceEqual([marker]),
            "Another conversation entered the isolated provider input.");
        probe.Start(marker);
        var outputs=input.EnumerateArray().Where(item=>Text(item,"type")=="function_call_output").ToArray();
        string name;
        string arguments;
        var terminal=false;
        if(outputs.Length==0)
        {
            await probe.EnterAsync(marker,cancellationToken);
            name="forms_assistant-candidates";
            arguments="{}";
        }
        else
        {
            var last=outputs[^1];
            var callId=RequiredText(last,"call_id");
            var call=input.EnumerateArray().Single(item=>Text(item,"type")=="function_call" && RequiredText(item,"call_id")==callId);
            using var observed=JsonDocument.Parse(RequiredText(last,"output"));
            var value=observed.RootElement;
            switch(RequiredText(call,"name"))
            {
                case "forms_assistant-candidates":
                    Require(RequiredText(value,"state")=="complete","Expected a complete real candidate read.");
                    var form=value.GetProperty("payload").GetProperty("forms").EnumerateArray().Single();
                    var field=form.GetProperty("questions").EnumerateArray().Single(item=>RequiredText(item,"type")=="shortText");
                    Require(field.GetProperty("maxLength").GetInt32()>=marker.Length,"Observed field cannot hold the isolated fact.");
                    name="forms_propose-registration";
                    arguments=JsonSerializer.Serialize(new
                    {
                        definitionId=RequiredGuid(form,"definitionId"),versionId=RequiredGuid(form,"versionId"),
                        values=new[]{new{fieldId=RequiredGuid(field,"fieldId"),type="shortText",value=marker}},
                        reason="La persona describió una visita a "+marker+".",
                    });
                    break;
                case "forms_propose-registration":
                    Require(RequiredText(value,"state")=="proposed","Expected a real observed proposal.");
                    name="assistant_proposal";
                    arguments=JsonSerializer.Serialize(new{proposalId=RequiredGuid(value,"proposalId")});
                    terminal=true;
                    break;
                default:throw new InvalidOperationException("Unexpected isolation tool result.");
            }
        }
        Require(root.GetProperty("tools").EnumerateArray().Any(tool=>RequiredText(tool,"name")==name),
            "The isolation tool was not offered by the real coordinator.");
        var id=probe.NextCallId();
        var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new
        {
            status="completed",output=new[]{new{type="function_call",id="item-"+id,call_id=id,name,arguments,status="completed"}},
            usage=new{input_tokens=10,output_tokens=2},
        }),Encoding.UTF8,"application/json")};
        if(terminal)probe.Complete(marker);
        return response;
    }
    private static string? Text(JsonElement value,string property)=>value.TryGetProperty(property,out var text)?text.GetString():null;
    private static string RequiredText(JsonElement value,string property)=>Text(value,property) is {Length:>0} text
        ?text:throw new InvalidOperationException("Missing isolation protocol text.");
    private static Guid RequiredGuid(JsonElement value,string property)
    {
        var id=value.GetProperty(property).GetGuid();
        Require(id!=Guid.Empty,"Missing observed isolation identity.");
        return id;
    }
    private static void Require(bool condition,string message)
    {
        if(!condition)throw new InvalidOperationException(message);
    }
}