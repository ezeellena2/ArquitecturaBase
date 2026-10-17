namespace AssistantQualification;
internal static class ChannelOfflineChecks
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        var actor=new QualificationActor(Guid.NewGuid(),"offline","es","fictitious");
        var context=QualificationChannelContexts.Create(actor,Guid.NewGuid(),"manual");
        Check("channel-fixes-capabilities-without-home",context.AllowedCapabilities.Count==11 && context.AllowedCapabilities.All(capability=>!QualificationSeed.HomeCapabilities.Contains(capability.Id)));
        Check("manual-current-actor-permissions",context.RequiredPermissions.Actor.SequenceEqual(["whatsapp.read","whatsapp.send"]));
        var audio=QualificationChannelContexts.CreateAudio(actor.UserId,"es");
        Check("audio-only-read-no-tools",audio.SubjectUserId is null && audio.RequiredPermissions.Actor.SequenceEqual(["whatsapp.read"]) && audio.AllowedCapabilities.Count==0 && audio.AllowedTerminals.Count==0);
        var rejected=false;try{QualificationChannelContexts.Create(actor,Guid.NewGuid(),"invented");}catch(ArgumentException){rejected=true;}
        Check("unknown-scope-is-rejected",rejected);
        return(passed,failed);
        void Check(string id,bool valid){if(valid)passed++;else failed++;Console.WriteLine(id+":"+(valid?"pass":"fail"));}
    }
}
