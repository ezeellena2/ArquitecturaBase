namespace AssistantQualification;
internal static class ChannelOfflineChecks
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        var actor=new QualificationActor(Guid.NewGuid(),"offline","es","fictitious");
        var context=QualificationChannelContexts.Create(actor,Guid.NewGuid(),"manual");
        Check("channel-fixes-capabilities-without-home",context.AllowedCapabilities.Count==7 && context.AllowedCapabilities.All(capability=>!QualificationSeed.HomeCapabilities.Contains(capability.Id)));
        Check("manual-excludes-catalog-proposals",context.AllowedCapabilities.All(capability=>!capability.Id.StartsWith("forms.catalog-filters.",StringComparison.Ordinal)));
        var automatic=QualificationChannelContexts.Create(actor,context.SubjectUserId!.Value,"auto");
        Check("automatic-keeps-catalog-proposals",automatic.AllowedCapabilities.Count==9 && automatic.AllowedCapabilities.Count(capability=>capability.Id.StartsWith("forms.catalog-filters.",StringComparison.Ordinal))==2);
        using var inventory=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixtures","channel-cases.json")));
        var cases=inventory.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        var injections=cases.Where(item=>item.GetProperty("id").GetString()!.StartsWith("channel-injection-",StringComparison.Ordinal)).ToArray();
        Check("channel-inventory-keeps-twenty-four-plus-six-injections",cases.Length==30 && injections.Length==6
            && injections.GroupBy(item=>item.GetProperty("culture").GetString()).All(group=>group.Count()==3)
            && injections.GroupBy(item=>item.GetProperty("actorKind").GetString()).Count()==3
            && injections.All(item=>item.GetProperty("expectedPayload").GetString() is "registration" or "missing"));
        Check("manual-current-actor-permissions",context.RequiredPermissions.Actor.SequenceEqual(["whatsapp.read","whatsapp.send"]));
        var source = Guid.NewGuid();
        var captured = QualificationChannelContexts.Create(actor,context.SubjectUserId!.Value,"manual",source);
        Check("captured-context-fixes-exact-source-and-catalog",captured.SourceRecordId==source && captured.AllowedCapabilities.Count==5
            && captured.AllowedCapabilities.Any(capability=>capability.Id=="forms.suggestion-candidates")
            && captured.AllowedCapabilities.All(capability=>capability.Id!="forms.propose-registration"));
        var audio=QualificationChannelContexts.CreateAudio(actor.UserId,"es");
        Check("audio-only-read-no-tools",audio.SubjectUserId is null && audio.RequiredPermissions.Actor.SequenceEqual(["whatsapp.read"]) && audio.AllowedCapabilities.Count==0 && audio.AllowedTerminals.Count==0);
        var rejected=false;try{QualificationChannelContexts.Create(actor,Guid.NewGuid(),"invented");}catch(ArgumentException){rejected=true;}
        Check("unknown-scope-is-rejected",rejected);
        return(passed,failed);
        void Check(string id,bool valid){if(valid)passed++;else failed++;Console.WriteLine(id+":"+(valid?"pass":"fail"));}
    }
}
