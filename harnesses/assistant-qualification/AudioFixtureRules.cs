using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace AssistantQualification;
internal sealed record AudioCase(string Id,string Culture,string FileName,string ContentType,long ByteCount,string Sha256,string ExpectedTranscript,string[] RequiredWords);
// A dictated request's recording, synthesized like the Ogg/Wav fixtures and kept with its hash: what the browser uploads in Home
// (IA4, WebM or MP4) or a voice note of the WhatsApp automatic conversation (IA3-A, Ogg).
internal sealed record DictationAudioFixture(string FileName,string ContentType,long ByteCount,string Sha256,string ExpectedTranscript,string[] RequiredWords);
internal static class AudioFixtureRules
{
    // WhatsAppAudioStoragePolicy.MaximumNormalizationBytes: a larger note keeps its copy and stays No disponible, without AI work.
    public const int WhatsAppNormalizationBytes=1_000_000;
    // IA3-A: a voice note of the automatic conversation is an Ogg the app would normalize; the name and type agree.
    public static bool IsValidVoiceNote(DictationAudioFixture audio) =>
        audio.ByteCount is >0 and <=WhatsAppNormalizationBytes && audio.ContentType=="audio/ogg"
        && Regex.IsMatch(audio.FileName,"^[a-z0-9-]+\\.ogg$",RegexOptions.CultureInvariant)
        && Regex.IsMatch(audio.Sha256,"^[a-f0-9]{64}$",RegexOptions.CultureInvariant)
        && audio.ExpectedTranscript.Length is >0 and <=4000 && audio.RequiredWords.Length is >0 and <=20
        && audio.RequiredWords.All(word=>Tokens(word).Length==1 && Tokens(audio.ExpectedTranscript).Contains(Tokens(word)[0],StringComparer.Ordinal));
    // The header WhatsAppAudioStoragePolicy checks before keeping a note: the first Ogg page (version 0, with segments) opens with
    // an OpusHead of version 0-15, one or two channels and mapping family 0.
    public static bool IsVoiceNote(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<47 || !bytes[..4].SequenceEqual("OggS"u8) || bytes[4]!=0 || bytes[26]==0)return false;
        var offset=27+bytes[26];
        return bytes.Length>=offset+19 && bytes[27]>=19 && bytes.Slice(offset,8).SequenceEqual("OpusHead"u8)
            && bytes[offset+8]<=15 && bytes[offset+9] is 1 or 2 && bytes[offset+18]==0;
    }
    // Home uploads at most 1.000.000 bytes, WebM or MP4 by their bytes (AiAssistantAudioPolicy.Detect); the name and type agree.
    public static bool IsValidBrowser(DictationAudioFixture audio) =>
        audio.ByteCount is >0 and <=1_000_000
        && Regex.IsMatch(audio.FileName,"^[a-z0-9-]+\\.(webm|m4a)$",RegexOptions.CultureInvariant)
        && (Path.GetExtension(audio.FileName),audio.ContentType) is (".webm","audio/webm") or (".m4a","audio/mp4")
        && Regex.IsMatch(audio.Sha256,"^[a-f0-9]{64}$",RegexOptions.CultureInvariant)
        && audio.ExpectedTranscript.Length is >0 and <=4000 && audio.RequiredWords.Length is >0 and <=20
        && audio.RequiredWords.All(word=>Tokens(word).Length==1 && Tokens(audio.ExpectedTranscript).Contains(Tokens(word)[0],StringComparer.Ordinal));
    public static string? DetectBrowser(ReadOnlySpan<byte> head) =>
        head.Length>=4 && head[..4].SequenceEqual((ReadOnlySpan<byte>)[0x1A,0x45,0xDF,0xA3]) ? "audio/webm"
            : head.Length>=12 && head.Slice(4,4).SequenceEqual("ftyp"u8) ? "audio/mp4" : null;
    public static bool IsValid(AudioCase testCase) =>
        Regex.IsMatch(testCase.Id,"^[a-z0-9-]{1,100}$",RegexOptions.CultureInvariant)
        && testCase.Culture is "es" or "en" && testCase.ByteCount is >0 and <=25000000
        && Regex.IsMatch(testCase.FileName,"^[a-z0-9-]+\\.(ogg|wav)$",RegexOptions.CultureInvariant)
        && (Path.GetExtension(testCase.FileName),testCase.ContentType) is (".ogg","audio/ogg") or (".wav","audio/wav")
        && Regex.IsMatch(testCase.Sha256,"^[a-f0-9]{64}$",RegexOptions.CultureInvariant)
        && testCase.ExpectedTranscript.Length is >0 and <=4000 && testCase.RequiredWords.Length is >0 and <=20
        && testCase.RequiredWords.All(word=>Tokens(word).Length==1 && Tokens(testCase.ExpectedTranscript).Contains(Tokens(word)[0],StringComparer.Ordinal));
    public static bool Matches(string expected,string actual,IReadOnlyList<string> requiredWords)
    {
        if(expected.Length is 0 or >4000 || actual.Length is 0 or >4000)return false;
        var left=Tokens(expected);var right=Tokens(actual);
        if(left.Length==0 || right.Length==0 || requiredWords.Any(word=>!Tokens(word).All(token=>right.Contains(token,StringComparer.Ordinal))))return false;
        return left.SequenceEqual(right,StringComparer.Ordinal);
    }
    private static string[] Tokens(string value)
    {
        var normalized=new string(value.Normalize(NormalizationForm.FormD)
            .Where(character=>CharUnicodeInfo.GetUnicodeCategory(character)!=UnicodeCategory.NonSpacingMark)
            .Select(character=>char.IsLetterOrDigit(character)?char.ToLowerInvariant(character):' ').ToArray());
        return normalized.Split(' ',StringSplitOptions.RemoveEmptyEntries);
    }
}
