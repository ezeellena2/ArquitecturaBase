using System.Diagnostics.Metrics;

namespace AssistantQualification;

internal static class OfflineChecks
{
    public static int Run()
    {
        var checks = new Action[] { HomeOfflineChecks.Home_hours_accept_exact_twelve_hour_equivalents, HomeOfflineChecks.Home_hours_accept_exact_dotted_twelve_hour_equivalents, HomeOfflineChecks.Home_twelve_hour_format_keeps_inversion_period_negation_and_fragment_guards, HomeOfflineChecks.Home_counts_accept_Markdown_in_number_first_claims, HomeOfflineChecks.Home_counts_accept_Markdown_in_label_first_claims, HomeOfflineChecks.Home_Markdown_does_not_hide_wrong_negated_or_missing_facts, ContinuationOfflineChecks.Continuation_submits_only_cases_without_any_ledger, ContinuationOfflineChecks.Continuation_before_submit_uncertainty_keeps_its_existing_identity, ContinuationOfflineChecks.Continuation_refuses_a_ledger_outside_the_original_inventory, ContinuationOfflineChecks.Continuation_refuses_duplicate_case_identifiers, ContinuationOfflineChecks.Continuation_refuses_multiple_requests_for_the_same_case, ContinuationOfflineChecks.Continuation_home_reevaluation_changes_only_the_fact_check_and_assessment, ContinuationOfflineChecks.Continuation_home_reevaluation_preserves_other_failed_checks, HomeOfflineChecks.Home_counts_allow_negation_outside_the_observed_claims, HomeOfflineChecks.Home_counts_reject_a_numeric_contradiction_after_a_correct_claim, HomeOfflineChecks.Home_counts_still_reject_negation_in_a_repeated_claim, Qualification_public_configuration_supplies_the_existing_local_identity_settings, Qualification_runtime_disables_external_delivery_and_publication_workers, Diagnostic_settings_make_the_evaluation_connection_read_only, Diagnostic_settings_preserve_existing_identity_and_other_connections, HomeOfflineChecks.Home_identical_observed_counts_can_be_repeated, HomeOfflineChecks.Home_conflicting_observed_counts_fail_without_throwing, ChannelSemanticOfflineChecks.Channel_missing_topic_cannot_be_unrelated_to_the_request, ChannelSemanticOfflineChecks.Channel_unsupported_criterion_must_identify_the_requested_eyes_filter, ChannelSemanticOfflineChecks.Channel_registration_rejects_extra_known_scalar, ChannelSemanticOfflineChecks.Channel_registration_checks_choice_against_the_observed_catalog, ChannelSemanticOfflineChecks.Channel_clarification_cannot_substitute_an_unrequested_form, ChannelSemanticOfflineChecks.Channel_catalog_cannot_add_an_unrequested_filter, ChannelSemanticOfflineChecks.Channel_fallback_cannot_mix_a_question_language, ChannelSemanticOfflineChecks.Channel_fallback_cannot_translate_only_a_catalog_option, ChannelSemanticOfflineChecks.Channel_reads_cannot_belong_to_another_subject, ChannelSemanticOfflineChecks.Channel_reads_cannot_treat_an_incomplete_catalog_as_complete, ChannelSemanticOfflineChecks.Channel_exact_registration_keeps_the_public_fact, DataProtectionOfflineChecks.Private_keys_recover_protected_fixtures_after_a_new_host, DataProtectionOfflineChecks.Private_keys_do_not_lend_a_protector_to_another_run, Actual_sizes_are_observed_without_provider, Cases_do_not_reuse_old_measurements, HomeOfflineChecks.Home_count_labels_cannot_borrow_one_from_fourteen, HomeOfflineChecks.Home_counts_cannot_swap_active_and_inactive_labels, HomeOfflineChecks.Home_counts_cannot_negate_the_observed_fact, HomeOfflineChecks.Home_counts_match_both_supported_label_orders, HomeOfflineChecks.Home_hours_do_not_reverse_opening_and_closing, AudioOfflineChecks.Public_audio_fixtures_are_valid_and_cannot_escape_their_directory, AudioOfflineChecks.Transcript_checks_allow_punctuation_and_accents_but_reject_translation_or_truncation, AudioOfflineChecks.Transcript_checks_reject_lost_spanish_negation, AudioOfflineChecks.Transcript_checks_reject_lost_english_negation, AudioOfflineChecks.Four_audio_fixtures_keep_their_committed_hashes_and_exact_formats, PrivateAudioOfflineChecks.Private_audio_is_write_once_and_survives_a_new_adapter, PrivateAudioOfflineChecks.Private_audio_keys_cannot_escape_the_isolated_directory, LedgerOfflineChecks.Accepted_audio_request_cannot_change_its_identity_or_context };
        var failed = 0;
        foreach (var check in checks.Concat(ExistingFixtureOfflineChecks.All).Concat(OptionalChoiceOfflineChecks.All))
        {
            try { check(); Console.WriteLine(check.Method.Name + ": pass"); }
            catch (InvalidOperationException) { failed++; Console.WriteLine(check.Method.Name + ": failed"); }
        }
        var channels=ChannelOfflineChecks.Run();
        return failed + channels.Failed == 0 ? 0 : 1;
    }

    private static void Qualification_public_configuration_supplies_the_existing_local_identity_settings()
    {
        var repository=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var configuration=QualificationHost.PublicConfiguration(repository).Build();
        Require(!string.IsNullOrEmpty(configuration["Authentication:LoginCode:HashKey"])
            && configuration.GetSection("Authentication:Clients:Web:RedirectUris").GetChildren().Any()
            && configuration.GetSection("Authentication:Clients:Web:PostLogoutRedirectUris").GetChildren().Any());
    }

    private static void Qualification_runtime_disables_external_delivery_and_publication_workers()
    {
        var settings=DiagnosticSettings();var runtime=QualificationHost.RuntimeOverrides(settings);
        Require(runtime["Email:Delivery"]=="PickupDirectory"
            && runtime["Email:PickupDirectory"]==Path.Combine(settings.OutputDirectory,"mail-unused")
            && Uri.TryCreate(runtime["Authentication:Issuer"],UriKind.Absolute,out var origin)
            && origin.Scheme=="https" && origin.Host=="qualification.example.invalid"
            && runtime["WhatsApp:PhoneNumberId"]==""
            && runtime["BusinessForms:PublicationWorker:ProcessInBackground"]=="false"
            && runtime["BusinessForms:ActionWorker:ProcessInBackground"]=="false");
    }

    private static QualificationSettings DiagnosticSettings() => new("fictitious_run","C:/fictitious/repository","C:/fictitious/output",
        "Host=localhost;Database=appdb;Username=fictitious",
        "Host=localhost;Database=assistant_qualification_fictitious_run;Username=fictitious",
        "localhost:6379");

    private static void Diagnostic_settings_make_the_evaluation_connection_read_only()
    {
        var readOnly = DiagnosticSettings().ForReadOnlyDiagnostic();
        Require(new Npgsql.NpgsqlConnectionStringBuilder(readOnly.EvaluationConnectionString).Options=="-c default_transaction_read_only=on");
    }
    private static void Diagnostic_settings_preserve_existing_identity_and_other_connections()
    {
        var original=DiagnosticSettings();var readOnly=original.ForReadOnlyDiagnostic();
        var before=new Npgsql.NpgsqlConnectionStringBuilder(original.EvaluationConnectionString);
        var after=new Npgsql.NpgsqlConnectionStringBuilder(readOnly.EvaluationConnectionString);
        Require(before.Host==after.Host && before.Port==after.Port && before.Database==after.Database && before.Username==after.Username
            && original.RunId==readOnly.RunId && original.OutputDirectory==readOnly.OutputDirectory
            && original.SourceConnectionString==readOnly.SourceConnectionString && original.RedisConnectionString==readOnly.RedisConnectionString);
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
