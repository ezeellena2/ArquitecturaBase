using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace AssistantQualification;
internal sealed record AudioCase(string Id,string Culture,string FileName,string ContentType,long ByteCount,string Sha256,string ExpectedTranscript,string[] RequiredWords);
internal static class AudioFixtureRules
{
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
        var tolerance=Math.Max(1,left.Length/5);
        if(Math.Abs(left.Length-right.Length)>tolerance)return false;
        var previous=Enumerable.Range(0,right.Length+1).ToArray();var current=new int[right.Length+1];
        for(var row=1;row<=left.Length;row++)
        {
            current[0]=row;
            for(var column=1;column<=right.Length;column++)
                current[column]=Math.Min(Math.Min(previous[column]+1,current[column-1]+1),previous[column-1]+(left[row-1]==right[column-1]?0:1));
            (current,previous)=(previous,current);
        }
        return previous[right.Length]<=tolerance;
    }
    private static string[] Tokens(string value)
    {
        var normalized=new string(value.Normalize(NormalizationForm.FormD)
            .Where(character=>CharUnicodeInfo.GetUnicodeCategory(character)!=UnicodeCategory.NonSpacingMark)
            .Select(character=>char.IsLetterOrDigit(character)?char.ToLowerInvariant(character):' ').ToArray());
        return normalized.Split(' ',StringSplitOptions.RemoveEmptyEntries);
    }
}
