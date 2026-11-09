using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace AssistantQualification;

// IA4 dictated Home requests: browser-format fixtures, the isolated private copy, the candidate STT profile and the checks of
// the normalization and of the turn that takes its transcript from the server. No provider, database or DI.
internal static class DictationOfflineChecks
{
    public static readonly Action[] All = [Dictated_cases_cover_both_browser_formats_in_both_cultures,
        Browser_audio_fixtures_keep_their_committed_hashes_formats_and_home_limit, Browser_audio_rules_reject_wrong_pairs_sizes_and_escapes,
        Dictated_expectations_come_from_the_spoken_transcript, Home_audio_private_copies_are_write_once_with_their_media_type,
        Browser_audio_profile_matches_its_public_candidate, Home_normalization_checks_accept_the_browser_profile_and_reject_drift,
        Dictated_turn_requires_the_server_transcript, Home_audio_ledger_scope_is_only_its_actor_channel_and_culture];

    private static readonly string[] Cultures = ["es", "en"];
    private static HomeCase[] Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "home-authoring-cases.json")));
        return document.RootElement.GetProperty("cases").Deserialize<HomeCase[]>(JsonSerializerOptions.Web)!;
    }
    private static HomeCase[] Dictated() => Cases().Where(testCase => testCase.Audio is not null).ToArray();
    private static DictationAudioFixture Sample() => new("authoring-es.webm", "audio/webm", 44183, new string('a', 64),
        "Armame un formulario que se llame Parte de lluvias.", ["formulario", "lluvias"]);

    public static void Dictated_cases_cover_both_browser_formats_in_both_cultures()
    {
        var dictated = Dictated();
        Require(dictated.Length == 4 && dictated.All(testCase => testCase.Question.Length == 0 && testCase.ActorKind == "author"
            && testCase.Id == $"home-authoring-dictated-{(testCase.Audio!.ContentType == "audio/webm" ? "webm" : "mp4")}-{testCase.Culture}"
            && testCase.Authoring?.Capability == "forms.authoring.propose-form" && testCase.ExpectedKind == "proposal"));
        Require(Cultures.All(culture => dictated.Count(testCase => testCase.Culture == culture && testCase.Audio!.ContentType == "audio/webm") == 1
            && dictated.Count(testCase => testCase.Culture == culture && testCase.Audio!.ContentType == "audio/mp4") == 1));
        // Every typed case keeps its question and no recording; a dictated one is only what it says.
        Require(Cases().Where(testCase => testCase.Audio is null).All(testCase => testCase.Question.Length is > 0 and <= 4000));
    }

    public static void Browser_audio_fixtures_keep_their_committed_hashes_formats_and_home_limit()
    {
        foreach (var testCase in Dictated())
        {
            var audio = testCase.Audio!;
            Require(AudioFixtureRules.IsValidBrowser(audio));
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "audio", audio.FileName));
            Require(bytes.LongLength == audio.ByteCount && bytes.LongLength <= SubmitAiAssistantAudioRequest.MaximumBytes
                && EvidenceWriter.HashPublicBytes(bytes) == audio.Sha256 && AudioFixtureRules.DetectBrowser(bytes) == audio.ContentType);
        }
        Require(Dictated().Select(testCase => testCase.Audio!.FileName).Distinct(StringComparer.Ordinal).Count() == 4);
    }

    public static void Browser_audio_rules_reject_wrong_pairs_sizes_and_escapes()
    {
        Require(AudioFixtureRules.IsValidBrowser(Sample()) && AudioFixtureRules.IsValidBrowser(Sample() with { FileName = "authoring-es.m4a", ContentType = "audio/mp4" }));
        Require(!AudioFixtureRules.IsValidBrowser(Sample() with { ContentType = "audio/mp4" }));
        Require(!AudioFixtureRules.IsValidBrowser(Sample() with { FileName = "authoring-es.ogg", ContentType = "audio/ogg" }));
        Require(!AudioFixtureRules.IsValidBrowser(Sample() with { FileName = "../authoring-es.webm" }));
        Require(!AudioFixtureRules.IsValidBrowser(Sample() with { ByteCount = SubmitAiAssistantAudioRequest.MaximumBytes + 1 }));
        Require(!AudioFixtureRules.IsValidBrowser(Sample() with { Sha256 = "not-a-hash" }));
        Require(!AudioFixtureRules.IsValidBrowser(Sample() with { RequiredWords = ["planilla"] }));
        Require(AudioFixtureRules.DetectBrowser([0x1A, 0x45, 0xDF, 0xA3, 0x9F]) == "audio/webm"
            && AudioFixtureRules.DetectBrowser([0, 0, 0, 0x1C, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', (byte)' ']) == "audio/mp4"
            && AudioFixtureRules.DetectBrowser("OggS\0\u0002\0\0\0\0\0\0"u8) is null && AudioFixtureRules.DetectBrowser([0x1A, 0x45]) is null);
    }

    public static void Dictated_expectations_come_from_the_spoken_transcript()
    {
        foreach (var testCase in Dictated())
        {
            var (audio, expected) = (testCase.Audio!, testCase.Authoring!);
            Require(AuthoringOracle.ContainsPhrase(audio.ExpectedTranscript, expected.Name!)
                && expected.Questions!.All(question => AuthoringOracle.ContainsPhrase(audio.ExpectedTranscript, question.Label))
                && AuthoringOracle.ContainsPhrase(audio.ExpectedTranscript, testCase.Culture == "es" ? "obligatoria" : "required")
                && AudioFixtureRules.Matches(audio.ExpectedTranscript, audio.ExpectedTranscript.ToUpperInvariant().Replace(",", "", StringComparison.Ordinal), audio.RequiredWords));
        }
    }

    public static void Home_audio_private_copies_are_write_once_with_their_media_type() => PrivateAsync().GetAwaiter().GetResult();
    private static async Task PrivateAsync()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "offline-home-audio-" + Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var store = new QualificationPrivateFiles(directory);
            var webm = "ai/assistant/audio/" + Guid.NewGuid().ToString("N") + ".webm";var mp4 = "ai/assistant/audio/" + Guid.NewGuid().ToString("N") + ".m4a";
            using (var first = new MemoryStream(Encoding.UTF8.GetBytes("public fixture one"))) Require(await store.WriteOnceAsync(webm, first, "audio/webm", CancellationToken.None));
            using (var second = new MemoryStream(Encoding.UTF8.GetBytes("public fixture two"))) Require(!await store.WriteOnceAsync(webm, second, "audio/webm", CancellationToken.None));
            using (var third = new MemoryStream(Encoding.UTF8.GetBytes("public fixture three"))) Require(await store.WriteOnceAsync(mp4, third, "audio/mp4", CancellationToken.None));
            await using (var read = await new QualificationPrivateFiles(directory).OpenReadAsync(webm, CancellationToken.None))
            {
                Require(read is not null);
                using var reader = new StreamReader(read!);Require(await reader.ReadToEndAsync() == "public fixture one");
            }
            foreach (var (key, type) in new[] { (webm.Replace(".webm", ".m4a", StringComparison.Ordinal), "audio/webm"), ("ai/assistant/audio/../outside.webm", "audio/webm"),
                ("ai/other/" + Guid.NewGuid().ToString("N") + ".webm", "audio/webm"), ("ai/assistant/audio/" + Guid.NewGuid().ToString("N") + ".ogg", "audio/ogg") })
            {
                var rejected = false;
                try { using var content = new MemoryStream([1]); await store.WriteOnceAsync(key, content, type, CancellationToken.None); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    public static void Browser_audio_profile_matches_its_public_candidate()
    {
        Require(QualificationHost.AudioProfiles.Length == 2);
        var ogg = QualificationHost.AudioProfiles.Single(profile => profile.Id == "openai-stt-candidate");
        Require(ogg is { Provider: AiProvider.OpenAI, Model: "gpt-transcribe", Version: 1, MaximumAudioBytes: 25000000 } && ogg.ContentTypes.SequenceEqual(["audio/ogg", "audio/wav"]));
        var browser = QualificationHost.AudioProfiles.Single(profile => profile.Id == "openai-stt-browser-candidate");
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "audio-browser-candidate-profile.json")));
        var root = document.RootElement;
        Require(browser.Provider == AiProvider.OpenAI && root.GetProperty("provider").GetString() == "OpenAI"
            && browser.Model == root.GetProperty("model").GetString() && browser.Version == root.GetProperty("version").GetInt32()
            && browser.ContentTypes.SequenceEqual(root.GetProperty("contentTypes").EnumerateArray().Select(item => item.GetString()!))
            && browser.MaximumAudioBytes == root.GetProperty("maximumAudioBytes").GetInt32()
            && browser.MaximumAudioBytes >= SubmitAiAssistantAudioRequest.MaximumBytes
            && browser.ContentTypes.All(type => type is "audio/webm" or "audio/mp4") && browser.ContentTypes.Distinct(StringComparer.Ordinal).Count() == 2);
    }

    private static TurnMetadata Normalized(string profile = "openai-stt-browser-candidate", string channel = "home-audio") =>
        new(1, 0, 0, null, null, true, 11.0m, new(profile, 1, "public", "audio-none-v1", 1, channel), []);

    public static void Home_normalization_checks_accept_the_browser_profile_and_reject_drift()
    {
        var audio = Dictated().First(testCase => testCase.Culture == "es").Audio!;
        var completed = new AiAssistantAudioResponse(Guid.NewGuid(), "completed", audio.ExpectedTranscript.ToLowerInvariant(), null, 2);
        bool Accepted(AiAssistantAudioResponse response, TurnMetadata technical)
        {
            var checks = HomeRunner.AudioChecks(audio, response, technical);
            return checks.Count > 0 && checks.All(check => check.Passed) && checks.Select(check => check.Id).Distinct(StringComparer.Ordinal).Count() == checks.Count;
        }
        Require(Accepted(completed, Normalized()));
        Require(!Accepted(completed with { State = "failed" }, Normalized()));
        Require(!Accepted(completed with { Transcript = null }, Normalized()));
        Require(!Accepted(completed with { Transcript = "Armame un formulario que se llame Parte de lluvias." }, Normalized()));
        Require(!Accepted(completed with { Transcript = "Build me a form named Rain report, with these questions: Place, short text, required; and Rainfall, number, required." }, Normalized()));
        Require(!Accepted(completed, Normalized(profile: "openai-stt-candidate")));
        Require(!Accepted(completed, Normalized(channel: "whatsapp-manual")));
        Require(!Accepted(completed, Normalized() with { ProviderSteps = 2 }));
        Require(!Accepted(completed, Normalized() with { ToolCalls = 1, Capabilities = [new("forms.authoring.forms", 1, 10, "complete")] }));
        Require(!Accepted(completed, Normalized() with { OfferedCapabilities = ["forms.authoring.forms"] }));
        Require(!Accepted(completed, Normalized() with { AudioSeconds = -1 }));
    }

    public static void Dictated_turn_requires_the_server_transcript()
    {
        var audioRequestId = Guid.NewGuid();const string transcript = "Armame un formulario que se llame Parte de lluvias.";
        var result = new AiAssistantTurnResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 1, 1, "completed", "completed", "allowed", "",
            new AiAssistantAnswer("proposal", "Texto ficticio.", []), [], null, null, "es", "America/Argentina/Buenos_Aires", DateTime.UnixEpoch, DateTime.UnixEpoch, 2)
        { Audio = new AiAssistantTurnAudioResponse(audioRequestId, transcript) };
        bool Accepted(AiAssistantTurnResponse turn, Guid id, string heard)
        {
            var checks = new List<CheckEvidence>();HomeRunner.AddDictationChecks(checks, turn, id, heard);
            return checks.Count > 0 && checks.All(check => check.Passed);
        }
        Require(Accepted(result, audioRequestId, transcript));
        Require(!Accepted(result with { Audio = null }, audioRequestId, transcript));
        Require(!Accepted(result, Guid.NewGuid(), transcript));
        Require(!Accepted(result, audioRequestId, transcript + " Agregá una pregunta más."));
        Require(!Accepted(result with { Question = "Armame un formulario." }, audioRequestId, transcript));
    }

    public static void Home_audio_ledger_scope_is_only_its_actor_channel_and_culture()
    {
        var entry = new QualificationLedgerEntry("home-authoring-dictated-webm-es-audio", Guid.NewGuid(), Guid.NewGuid(), null, null, "accepted")
            { OperationKind = "home-audio", ChannelId = "home-audio", Culture = "es" };
        Require(QualificationChannelContexts.IsHomeAudio(entry) && QualificationChannelContexts.IsHomeAudio(entry with { Culture = "en" }));
        Require(!QualificationChannelContexts.IsHomeAudio(entry with { ChannelId = "whatsapp-manual" }) && !QualificationChannelContexts.IsHomeAudio(entry with { Culture = "fr" })
            && !QualificationChannelContexts.IsHomeAudio(entry with { SubjectUserId = Guid.NewGuid() }) && !QualificationChannelContexts.IsHomeAudio(entry with { OperationKind = "audio" })
            && !QualificationChannelContexts.IsHomeAudio(entry with { ChannelKind = "author" }) && !QualificationChannelContexts.IsHomeAudio(entry with { PreviousRequestId = Guid.NewGuid() })
            && !QualificationChannelContexts.IsHomeAudio(entry with { CaseId = "home-authoring-dictated-webm-es" }));
        // A channel ledger is never read as a Home recording, nor the other way around.
        var rejected = false;
        try { _ = QualificationChannelContexts.Resume(entry); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected);
    }

    private static void Require(bool valid) { if (!valid) throw new InvalidOperationException("Offline dictation check failed."); }
}
