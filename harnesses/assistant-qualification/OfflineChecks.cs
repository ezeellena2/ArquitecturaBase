using System.Diagnostics.Metrics;

namespace AssistantQualification;

internal static class OfflineChecks
{
    public static int Run()
    {
        var checks = new Action[] { DataProtectionOfflineChecks.Private_keys_recover_protected_fixtures_after_a_new_host, DataProtectionOfflineChecks.Private_keys_do_not_lend_a_protector_to_another_run, Actual_sizes_are_observed_without_provider, Cases_do_not_reuse_old_measurements, AudioOfflineChecks.Public_audio_fixtures_are_valid_and_cannot_escape_their_directory, AudioOfflineChecks.Transcript_checks_allow_punctuation_and_accents_but_reject_translation_or_truncation, AudioOfflineChecks.Four_audio_fixtures_keep_their_committed_hashes_and_exact_formats, PrivateAudioOfflineChecks.Private_audio_is_write_once_and_survives_a_new_adapter, PrivateAudioOfflineChecks.Private_audio_keys_cannot_escape_the_isolated_directory, LedgerOfflineChecks.Accepted_audio_request_cannot_change_its_identity_or_context };
        var failed = 0;
        foreach (var check in checks)
        {
            try { check(); Console.WriteLine(check.Method.Name + ": pass"); }
            catch (InvalidOperationException) { failed++; Console.WriteLine(check.Method.Name + ": failed"); }
        }
        var channels=ChannelOfflineChecks.Run();
        return failed + channels.Failed == 0 ? 0 : 1;
    }

    private static void Actual_sizes_are_observed_without_provider()
    {
        using var measurements = new QualificationMeasurements();
        using var meter = new Meter("ArquitecturaBase.AiAssistant", "1.0");
        var context = meter.CreateHistogram<int>("ai.assistant.context.bytes");
        var continuation = meter.CreateHistogram<int>("ai.assistant.continuation.bytes");
        using var unrelated = new Meter("Fictitious.Other");
        var ignored = unrelated.CreateHistogram<int>("ai.assistant.context.bytes");
        measurements.BeginCase();
        context.Record(420); context.Record(920); continuation.Record(300); ignored.Record(99999);
        var result = measurements.EndCase();
        Require(result.MaximumContextBytes == 920 && result.MaximumContinuationBytes == 300);
    }

    private static void Cases_do_not_reuse_old_measurements()
    {
        using var measurements = new QualificationMeasurements();
        using var meter = new Meter("ArquitecturaBase.AiAssistant", "1.0");
        var context = meter.CreateHistogram<int>("ai.assistant.context.bytes");
        context.Record(10000);
        measurements.BeginCase(); context.Record(120);
        Require(measurements.EndCase().MaximumContextBytes == 120);
        context.Record(20000); measurements.BeginCase();
        var empty = measurements.EndCase();
        Require(empty.MaximumContextBytes is null && empty.MaximumContinuationBytes is null);
    }

    private static void Require(bool valid) { if (!valid) throw new InvalidOperationException("Offline check failed."); }
}
