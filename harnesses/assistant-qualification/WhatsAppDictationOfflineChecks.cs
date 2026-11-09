using System.Text.Json;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace AssistantQualification;

// IA3-A by voice: a fictitious Ogg voice note of the person's own automatic WhatsApp conversation, normalized with the Ogg candidate
// (openai-stt-candidate@1) as the app captures it, and the interpretation that reads the server transcript with forms.authoring.*.
// No provider, database or DI.
internal static class WhatsAppDictationOfflineChecks
{
    public static readonly Action[] All = [WhatsApp_dictated_cases_cover_one_voice_note_per_culture,
        WhatsApp_voice_notes_keep_their_committed_hashes_and_header, WhatsApp_voice_note_rules_reject_wrong_pairs_sizes_and_headers,
        WhatsApp_dictated_expectations_come_from_the_spoken_note, WhatsApp_automatic_audio_context_is_the_own_conversation_capture,
        WhatsApp_dictation_normalization_checks_accept_the_ogg_candidate_and_reject_drift, WhatsApp_interpretation_takes_the_server_transcript,
        WhatsApp_dictation_ledger_resumes_only_the_automatic_own_conversation, Channel_dictation_case_set_runs_only_the_dictated_cases,
        Channel_sets_keep_each_person_within_the_daily_quota];

    private static readonly string[] Cultures = ["es", "en"];
    private static T[] Cases<T>(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", file)));
        return document.RootElement.GetProperty("cases").Deserialize<T[]>(JsonSerializerOptions.Web)!;
    }
    private static ChannelCase[] Cases() => Cases<ChannelCase>("channel-authoring-cases.json");
    private static ChannelCase[] Dictated() => Cases().Where(testCase => testCase.Audio is not null).ToArray();
    private static ChannelCase Dictated(string culture) => Dictated().Single(testCase => testCase.Culture == culture);
    private static DictationAudioFixture Sample() => new("authoring-es.ogg", "audio/ogg", 41334, new string('a', 64),
        "Armame un formulario que se llame Parte de lluvias.", ["formulario", "lluvias"]);

    public static void WhatsApp_dictated_cases_cover_one_voice_note_per_culture()
    {
        var dictated = Dictated();
        Require(dictated.Length == 2 && dictated.All(testCase => testCase.Id == "channel-authoring-dictated-ogg-" + testCase.Culture
            && testCase.Question.Length == 0 && testCase.ActorKind == "author" && testCase.ExpectedPayload == "authoring-form"
            && testCase.Audio!.ContentType == "audio/ogg" && testCase.Authoring is { Capability: "forms.authoring.propose-form", Continues: null }
            && testCase.RequiredTools.SequenceEqual(["forms.authoring.forms", "forms.authoring.propose-form"])));
        Require(Cultures.All(culture => dictated.Count(testCase => testCase.Culture == culture) == 1));
        // Every typed case keeps its question and no recording; a dictated one is only what it says.
        Require(Cases().Where(testCase => testCase.Audio is null).All(testCase => testCase.Question.Length is > 0 and <= 4000));
    }

    public static void WhatsApp_voice_notes_keep_their_committed_hashes_and_header()
    {
        Require(Dictated().Length == 2);
        foreach (var testCase in Dictated())
        {
            var audio = testCase.Audio!;
            Require(AudioFixtureRules.IsValidVoiceNote(audio));
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "audio", audio.FileName));
            Require(bytes.LongLength == audio.ByteCount && EvidenceWriter.HashPublicBytes(bytes) == audio.Sha256 && AudioFixtureRules.IsVoiceNote(bytes));
        }
        // Their own recordings: neither the visit notes of audio-es-en nor each other.
        var names = Dictated().Select(testCase => testCase.Audio!.FileName).ToArray();
        Require(names.Distinct(StringComparer.Ordinal).Count() == 2 && names.All(name => !name.StartsWith("visit-", StringComparison.Ordinal)));
    }

    public static void WhatsApp_voice_note_rules_reject_wrong_pairs_sizes_and_headers()
    {
        Require(AudioFixtureRules.IsValidVoiceNote(Sample()));
        Require(!AudioFixtureRules.IsValidVoiceNote(Sample() with { FileName = "authoring-es.webm", ContentType = "audio/webm" }));
        Require(!AudioFixtureRules.IsValidVoiceNote(Sample() with { FileName = "authoring-es.wav", ContentType = "audio/wav" }));
        Require(!AudioFixtureRules.IsValidVoiceNote(Sample() with { ContentType = "audio/mp4" }));
        Require(!AudioFixtureRules.IsValidVoiceNote(Sample() with { FileName = "../authoring-es.ogg" }));
        Require(!AudioFixtureRules.IsValidVoiceNote(Sample() with { ByteCount = AudioFixtureRules.WhatsAppNormalizationBytes + 1 }));
        Require(!AudioFixtureRules.IsValidVoiceNote(Sample() with { Sha256 = "not-a-hash" }));
        Require(!AudioFixtureRules.IsValidVoiceNote(Sample() with { RequiredWords = ["planilla"] }));
        // The header the app checks before keeping a note (WhatsAppAudioStoragePolicy): the first Ogg page opens with OpusHead,
        // mono or stereo, family 0. Offsets: 26 segments, 27 first segment, 28 OpusHead, 36 version, 37 channels, 46 family.
        var note = VoiceNoteHeader();
        Require(AudioFixtureRules.IsVoiceNote(note));
        Require(!AudioFixtureRules.IsVoiceNote(note.AsSpan(0, 46)));
        Require(!AudioFixtureRules.IsVoiceNote(With(note, 0, (byte)'X')) && !AudioFixtureRules.IsVoiceNote(With(note, 4, 1)) && !AudioFixtureRules.IsVoiceNote(With(note, 26, 0)));
        Require(!AudioFixtureRules.IsVoiceNote(With(note, 27, 18)) && !AudioFixtureRules.IsVoiceNote(With(note, 28, (byte)'V')));
        Require(!AudioFixtureRules.IsVoiceNote(With(note, 36, 16)) && !AudioFixtureRules.IsVoiceNote(With(note, 37, 3)) && !AudioFixtureRules.IsVoiceNote(With(note, 37, 0))
            && !AudioFixtureRules.IsVoiceNote(With(note, 46, 1)));
        Require(!AudioFixtureRules.IsVoiceNote([0x1A, 0x45, 0xDF, 0xA3, .. new byte[60]]) && !AudioFixtureRules.IsVoiceNote([0, 0, 0, 0x1C, .. "ftypM4A "u8, .. new byte[60]]));
    }
    private static byte[] VoiceNoteHeader()
    {
        var header = new byte[47];
        "OggS"u8.CopyTo(header);
        header[26] = 1; header[27] = 19;
        "OpusHead"u8.CopyTo(header.AsSpan(28));
        header[36] = 1; header[37] = 1;
        return header;
    }
    private static byte[] With(byte[] bytes, int index, byte value) { var copy = bytes.ToArray(); copy[index] = value; return copy; }

    public static void WhatsApp_dictated_expectations_come_from_the_spoken_note()
    {
        Require(Dictated().Length == 2);
        foreach (var testCase in Dictated())
        {
            var (audio, expected) = (testCase.Audio!, testCase.Authoring!);
            Require(AuthoringOracle.ContainsPhrase(audio.ExpectedTranscript, expected.Name!)
                && expected.Questions!.All(question => AuthoringOracle.ContainsPhrase(audio.ExpectedTranscript, question.Label))
                && AuthoringOracle.ContainsPhrase(audio.ExpectedTranscript, testCase.Culture == "es" ? "obligatoria" : "required")
                && AudioFixtureRules.Matches(audio.ExpectedTranscript, audio.ExpectedTranscript.ToUpperInvariant().Replace(",", "", StringComparison.Ordinal), audio.RequiredWords));
            // The note says what the Home dictation of its culture says: same synthesized speech, now as a WhatsApp voice note.
            var home = Cases<HomeCase>("home-authoring-cases.json").Single(item => item.Id == "home-authoring-dictated-webm-" + testCase.Culture);
            Require(home.Audio!.ExpectedTranscript == audio.ExpectedTranscript && home.Authoring!.Name == expected.Name
                && JsonSerializer.Serialize(home.Authoring.Questions) == JsonSerializer.Serialize(expected.Questions));
        }
    }

    public static void WhatsApp_automatic_audio_context_is_the_own_conversation_capture()
    {
        var person = Guid.NewGuid();
        var context = QualificationChannelContexts.CreateAutomaticAudio(person, "en");
        // What WhatsAppAudioAccessPolicy.CaptureAsync builds for an automatic note: the person as Actor and Subject, nothing required or offered.
        Require(context is { ChannelId: "whatsapp-assistant", Culture: "en", TimeZoneId: "America/Argentina/Buenos_Aires",
                InteractionMode: AssistantInteractionMode.Assistant, ChannelConversationId: null, SourceRecordId: null }
            && context.ActorUserId == person && context.SubjectUserId == person
            && context.RequiredPermissions.Actor.Count == 0 && context.RequiredPermissions.Subject.Count == 0
            && context.AllowedCapabilities.Count == 0 && context.AllowedTerminals.Count == 0);
        // The read-only operator's manual note keeps its own context.
        Require(QualificationChannelContexts.CreateAudio(person, "es") is { ChannelId: "whatsapp-manual", SubjectUserId: null, InteractionMode: AssistantInteractionMode.Manual });
    }

    private static TurnMetadata Normalized(Guid subject, string profile = "openai-stt-candidate", string channel = "whatsapp-assistant") =>
        new(1, 0, 0, null, null, true, 11.0m, new(profile, 1, "public", "audio-none-v1", 1, channel), [])
        { InteractionMode = AssistantInteractionMode.Assistant, SubjectUserId = subject };

    public static void WhatsApp_dictation_normalization_checks_accept_the_ogg_candidate_and_reject_drift()
    {
        var audio = Dictated("es").Audio!;var subject = Guid.NewGuid();
        var completed = new AssistantOperationResponse(Guid.NewGuid(), "completed", null,
            new("openai-stt-candidate", 1, "audio-none-v1", 1, DateTime.UnixEpoch, "whatsapp-assistant"), null)
            { NormalizedAudio = new(audio.ExpectedTranscript.ToLowerInvariant(), ["es"]) };
        bool Accepted(AssistantOperationResponse result, TurnMetadata technical, bool sameConnection = true)
        {
            var checks = AudioRunner.DictationChecks(audio, result, technical, subject, sameConnection);
            return checks.Count > 0 && checks.All(check => check.Passed) && checks.Select(check => check.Id).Distinct(StringComparer.Ordinal).Count() == checks.Count;
        }
        Require(Accepted(completed, Normalized(subject)));
        Require(!Accepted(completed with { State = "failed" }, Normalized(subject)) && !Accepted(completed with { NormalizedAudio = null }, Normalized(subject)));
        Require(!Accepted(completed with { NormalizedAudio = new("Armame un formulario que se llame Parte de lluvias.", ["es"]) }, Normalized(subject)));
        Require(!Accepted(completed with { NormalizedAudio = new(Dictated("en").Audio!.ExpectedTranscript, ["en"]) }, Normalized(subject)));
        Require(!Accepted(completed, Normalized(subject, profile: "openai-stt-browser-candidate")));
        Require(!Accepted(completed, Normalized(subject, channel: "whatsapp-manual")) && !Accepted(completed, Normalized(subject, channel: "home-audio")));
        Require(!Accepted(completed with { Provenance = completed.Provenance! with { ChannelId = "whatsapp-manual" } }, Normalized(subject)));
        Require(!Accepted(completed, Normalized(subject) with { InteractionMode = AssistantInteractionMode.Manual })
            && !Accepted(completed, Normalized(subject) with { SubjectUserId = Guid.NewGuid() }) && !Accepted(completed, Normalized(subject) with { SubjectUserId = null }));
        Require(!Accepted(completed, Normalized(subject) with { ProviderSteps = 2 }));
        Require(!Accepted(completed, Normalized(subject) with { ToolCalls = 1, Capabilities = [new("forms.authoring.forms", 1, 10, "complete")] }));
        Require(!Accepted(completed, Normalized(subject) with { OfferedCapabilities = ["forms.authoring.propose-form"] }));
        Require(!Accepted(completed with { Proposal = new(Guid.NewGuid(), Guid.NewGuid(), new("forms.authoring.decline", 1), new FormAuthoringDecline("unclear", null, null, "")) },
            Normalized(subject)));
        Require(!Accepted(completed, Normalized(subject) with { AudioSeconds = -1 }) && !Accepted(completed, Normalized(subject), sameConnection: false));
    }

    public static void WhatsApp_interpretation_takes_the_server_transcript()
    {
        var testCase = Dictated("es");var subject = Guid.NewGuid();var interpretation = Guid.NewGuid();var noteConversation = Guid.NewGuid();
        const string heard = "armame un formulario que se llame parte de lluvias con estas preguntas lugar texto corto obligatoria y milimetros numero obligatoria";
        var normalization = new AudioObservation(new AssistantOperationResponse(Guid.NewGuid(), "completed", null, null, null) { NormalizedAudio = new(heard, ["es"]) },
            Normalized(subject) with { ConversationId = noteConversation }, 2, [], new(null, null));
        // What the engine sealed for the interpretation: the same person's automatic conversation, a first turn of its own.
        var turn = new TurnMetadata(3, 2, 512, null, null, true, null, new("gpt-6.1-sol-candidate-1", 1, "public", "capabilities-v1", 1, "whatsapp-assistant"), [])
            { InteractionMode = AssistantInteractionMode.Assistant, SubjectUserId = subject, ConversationId = Guid.NewGuid(), Ordinal = 1 };
        // The interpretation reads what the server heard (the completed note's Body in the app), never the public fixture's wording.
        Require(ChannelRunner.DictatedText(testCase, normalization) == heard && heard != testCase.Audio!.ExpectedTranscript);
        bool Accepted(ChannelCase dictated, string text, AudioObservation observed, Guid person, Guid requestId, TurnMetadata? interpreted = null)
        {
            var checks = new List<CheckEvidence>();ChannelRunner.AddDictationChecks(checks, dictated, text, observed, interpreted ?? turn, person, requestId);
            return checks.Count > 0 && checks.All(check => check.Passed);
        }
        Require(Accepted(testCase, heard, normalization, subject, interpretation));
        Require(!Accepted(testCase, testCase.Audio!.ExpectedTranscript, normalization, subject, interpretation));
        Require(!Accepted(testCase with { Question = "Armame un formulario." }, heard, normalization, subject, interpretation));
        Require(!Accepted(testCase, heard, normalization, Guid.NewGuid(), interpretation) && !Accepted(testCase, heard, normalization, subject, normalization.Result.RequestId));
        Require(!Accepted(testCase, heard, normalization with { Metadata = Normalized(subject, channel: "whatsapp-manual") }, subject, interpretation));
        Require(!Accepted(testCase, heard, normalization with { Metadata = Normalized(subject) with { InteractionMode = AssistantInteractionMode.Manual } }, subject, interpretation));
        Require(!Accepted(testCase, heard, normalization with { Result = normalization.Result with { State = "failed" } }, subject, interpretation));
        Require(!Accepted(testCase, heard, normalization, subject, interpretation, turn with { SubjectUserId = Guid.NewGuid() })
            && !Accepted(testCase, heard, normalization, subject, interpretation, turn with { InteractionMode = AssistantInteractionMode.Manual })
            && !Accepted(testCase, heard, normalization, subject, interpretation, turn with { ConversationId = noteConversation }));
        // Without a completed transcript there is nothing to interpret: the case stops before submitting.
        foreach (var (dictated, observed) in new[] { (testCase, normalization with { Result = normalization.Result with { State = "failed" } }),
            (testCase, normalization with { Result = normalization.Result with { NormalizedAudio = null } }),
            (testCase with { Question = "Armame un formulario." }, normalization), (testCase with { Audio = null }, normalization) })
        {
            var rejected = false;
            try { _ = ChannelRunner.DictatedText(dictated, observed); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected);
        }
    }

    public static void WhatsApp_dictation_ledger_resumes_only_the_automatic_own_conversation() => LedgerAsync().GetAwaiter().GetResult();
    private static async Task LedgerAsync()
    {
        var person = Guid.NewGuid();
        var entry = new QualificationLedgerEntry("channel-authoring-dictated-ogg-es-audio", Guid.NewGuid(), person, null, null, "before-submit")
            { OperationKind = "audio", ChannelId = "whatsapp-assistant", InteractionMode = AssistantInteractionMode.Assistant, Culture = "es", SubjectUserId = person };
        var resumed = QualificationChannelContexts.Resume(entry);
        Require(resumed is { ChannelId: "whatsapp-assistant", InteractionMode: AssistantInteractionMode.Assistant } && resumed.ActorUserId == person
            && resumed.SubjectUserId == person && resumed.AllowedCapabilities.Count == 0 && resumed.AllowedTerminals.Count == 0 && resumed.RequiredPermissions.Actor.Count == 0);
        foreach (var invalid in new[] { entry with { SubjectUserId = Guid.NewGuid() }, entry with { SubjectUserId = null }, entry with { InteractionMode = AssistantInteractionMode.Manual },
            entry with { InteractionMode = AssistantInteractionMode.Unspecified }, entry with { ChannelKind = "author" }, entry with { SourceRecordId = Guid.NewGuid() },
            entry with { PreviousRequestId = Guid.NewGuid() }, entry with { ChannelId = "home-audio" } })
        {
            var rejected = false;
            try { _ = QualificationChannelContexts.Resume(invalid); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected);
        }
        // The read-only operator's manual note still resumes as before.
        Require(QualificationChannelContexts.Resume(entry with { ChannelId = "whatsapp-manual", SubjectUserId = null, InteractionMode = AssistantInteractionMode.Manual })
            is { ChannelId: "whatsapp-manual", SubjectUserId: null, InteractionMode: AssistantInteractionMode.Manual });
        // An accepted note keeps its person and mode.
        var directory = Path.Combine(AppContext.BaseDirectory, "offline-ledger-" + Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var ledger = new SafeRunLedger(new("offline_run", AppContext.BaseDirectory, directory, "unused", "unused", "unused"));
            await ledger.SaveAsync(entry, CancellationToken.None);await ledger.SaveAsync(entry with { Phase = "accepted" }, CancellationToken.None);
            foreach (var changed in new[] { entry with { SubjectUserId = Guid.NewGuid() }, entry with { InteractionMode = AssistantInteractionMode.Manual } })
            {
                var rejected = false;
                try { await ledger.SaveAsync(changed, CancellationToken.None); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    public static void Channel_dictation_case_set_runs_only_the_dictated_cases()
    {
        var cases = Cases();
        Require(Dictated().Length == 2 && Program.SelectChannelCases("channel-dictation", cases).Select(testCase => testCase.Id).SequenceEqual(Dictated().Select(testCase => testCase.Id))
            && Program.SelectChannelCases("channel-authoring", cases).Length == cases.Length && Program.SelectChannelCases("ia-authoring", cases).Length == cases.Length);
        // run.ps1 offers exactly the sets the harness knows.
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var script = File.ReadAllText(Path.Combine(repository, "harnesses", "assistant-qualification", "run.ps1"));
        var offered = Regex.Matches(Regex.Match(script, @"\[ValidateSet\(([^)]*)\)\]").Groups[1].Value, "\"([a-z0-9-]+)\"").Select(match => match.Groups[1].Value);
        Require(Program.CaseSets.Contains("channel-dictation", StringComparer.Ordinal) && offered.SequenceEqual(Program.CaseSets));
    }

    // The engine accepts ten operations a day per person, channel and kind (interpretation or audio): AiAssistantSubmissionPolicy with
    // the default AiAssistantLimits the isolated host keeps. Counted with the runner's own choice of people.
    public static void Channel_sets_keep_each_person_within_the_daily_quota()
    {
        static QualificationActor Person(string culture) => new(Guid.NewGuid(), "offline", culture, "fictitious");
        QualificationActor[] Pair() => [Person("es"), Person("en")];
        var actors = new QualificationFixture([], "Supervisor")
            { ChannelAuthors = Pair(), VoiceAuthors = Pair(), Subjects = Pair(), Operators = Pair(), AudioActors = Pair() };
        var quota = new AiAssistantLimits().ActorDailyQuota;
        foreach (var (set, file) in new[] { ("channel-stage5", "channel-cases.json"), ("channel-authoring", "channel-authoring-cases.json"),
            ("ia-authoring", "channel-authoring-cases.json"), ("channel-dictation", "channel-authoring-cases.json") })
        {
            var accepted = Program.SelectChannelCases(set, Cases<ChannelCase>(file)).SelectMany(testCase =>
            {
                var (actor, subject) = ChannelRunner.People(testCase, actors);
                var interpretation = (actor.UserId, QualificationChannelContexts.Create(actor, subject.UserId, testCase.ActorKind).ChannelId, false);
                // A voice note is normalized first, in the same person's automatic conversation, with a budget of its own.
                (Guid, string, bool)[] operations = testCase.Audio is null ? [interpretation] : [interpretation, (actor.UserId, "whatsapp-assistant", true)];
                return operations;
            }).ToArray();
            Require(accepted.Length > 0 && accepted.GroupBy(operation => operation).All(group => group.Count() <= quota));
        }
    }

    private static void Require(bool valid) { if (!valid) throw new InvalidOperationException("Offline WhatsApp dictation check failed."); }
}
