using System.Text.Json;
namespace AssistantQualification;
internal static class AudioOfflineChecks
{
    private static AudioCase Sample()=>new("audio-ogg-es","es","visit-es.ogg","audio/ogg",25317,new string('a',64),"Quiero registrar una visita a la sucursal norte.",["visita","norte"]);
    public static void Public_audio_fixtures_are_valid_and_cannot_escape_their_directory()
    {
        Require(AudioFixtureRules.IsValid(Sample()));
        Require(!AudioFixtureRules.IsValid(Sample() with{FileName="../visit-es.ogg"}));
        Require(!AudioFixtureRules.IsValid(Sample() with{ContentType="audio/mpeg"}));
        Require(!AudioFixtureRules.IsValid(Sample() with{Sha256="not-a-hash"}));
        Require(!AudioFixtureRules.IsValid(Sample() with{ByteCount=25000001}));
    }
    public static void Transcript_checks_allow_punctuation_and_accents_but_reject_translation_or_truncation()
    {
        Require(AudioFixtureRules.Matches("Visité la sucursal norte. La puerta está cerrada.","Visite la sucursal norte, la puerta esta cerrada!",["sucursal","cerrada"]));
        Require(!AudioFixtureRules.Matches("La puerta de la sucursal norte está cerrada.","The entrance at the north branch is closed.",["puerta","cerrada"]));
        Require(!AudioFixtureRules.Matches("Quiero registrar una visita en la sucursal norte porque la puerta está cerrada.","Quiero registrar una visita.",["puerta","cerrada"]));
    }
    public static void Four_audio_fixtures_keep_their_committed_hashes_and_exact_formats()
    {
        using var document=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","audio-cases.json")));
        var cases=document.RootElement.GetProperty("cases").Deserialize<AudioCase[]>(JsonSerializerOptions.Web)!;Require(cases.Length==4);
        foreach(var testCase in cases)
        {
            Require(AudioFixtureRules.IsValid(testCase));var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","audio",testCase.FileName));
            Require(bytes.LongLength==testCase.ByteCount && EvidenceWriter.HashPublicBytes(bytes)==testCase.Sha256);
        }
    }
    private static void Require(bool valid){if(!valid)throw new InvalidOperationException("Offline audio qualification check failed.");}
}
