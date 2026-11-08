using System.Globalization;
using System.Resources;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace AssistantQualification;

// IA1-A/IA3-A oracles: every case's ideal shape passes and each finite deviation fails, without a provider, database or DI.
internal static class AuthoringOfflineChecks
{
    public static readonly Action[] All = [Authoring_fixtures_cover_the_stage_cases_in_both_cultures,
        Authoring_fixtures_stay_fictitious_and_generic, Authoring_fixtures_reference_known_capabilities_and_aliases,
        Authoring_ideal_proposals_pass_every_payload_check, Authoring_canonical_text_ignores_only_case_accents_and_punctuation,
        Authoring_form_rejects_an_extra_missing_or_reordered_question, Authoring_form_rejects_a_changed_type_requirement_or_option,
        Authoring_form_requires_the_existing_catalog_by_identity, Authoring_form_rejects_a_name_or_prefix_already_in_use,
        Authoring_form_keeps_the_requested_name_prefix_and_culture, Authoring_adjustment_keeps_the_previous_name_and_prefix,
        Authoring_catalog_rejects_wrong_value_type_code_option_or_fields, Authoring_decline_requires_the_checked_reason_and_target,
        Authoring_people_catalog_keeps_the_exact_supervisor_filters, Authoring_payload_of_another_owner_fails_every_stable_check,
        Authoring_reads_must_precede_the_proposal_in_the_same_turn, Authoring_rejection_must_be_followed_by_an_accepted_proposal,
        Authoring_turn_proposes_only_the_expected_capability, Authoring_offer_follows_the_account_permissions,
        Home_authoring_checks_accept_the_observed_proposal_turn, Home_authoring_checks_reject_a_mismatched_terminal_or_stamp,
        Home_authoring_checks_require_no_proposal_without_permission_or_details, Home_authoring_adjustment_requires_the_same_conversation,
        Channel_authoring_checks_accept_the_observed_proposal_and_decline, Channel_authoring_checks_reject_wrong_offer_or_reason,
        Channel_authoring_continuation_requires_the_next_turn_of_the_same_conversation, Channel_authoring_answer_follows_the_question_of_the_same_conversation,
        Home_instruction_hash_follows_the_sealed_version, Ledger_continuation_keeps_its_previous_request_identity];

    private const string IdealPrefix = "QAFIXTURE";
    private static readonly QualificationFixture Actors = new([], "Supervisor") { SupervisorRoleId = Guid.NewGuid() };
    private static readonly QualificationChannelFixture Fixture = new(
        QualificationFormSeed.FormNames.Select(text => new QualificationForm(Guid.NewGuid(), Guid.NewGuid(), text.Alias, new Dictionary<string, Guid>())
            { Names = Names(text.Spanish, text.English), Prefix = text.Prefix }).ToArray(), [], [])
    {
        Catalogs = QualificationFormSeed.CatalogNames.Select(text => new QualificationCatalog(text.Alias, Guid.NewGuid(), Names(text.Spanish, text.English))).ToArray(),
    };
    private static readonly HashSet<string> ProposalIds = new(StringComparer.Ordinal) { "forms.authoring.propose-form", "forms.authoring.propose-catalog",
        "forms.authoring.decline", "forms.catalog-filters.propose", "forms.missing-request.propose", "forms.propose-registration",
        "forms.clarify-registration", "forms.propose-suggestion", "whatsapp.no-reply", "whatsapp.decline-form" };
    private static readonly string[] HomeQueries = ["business.information", "roles.list", "roles.permissions", "system.help", "users.summary"];
    private static readonly string[] HomeAuthoring = ["forms.authoring.forms", "forms.authoring.catalogs", "forms.authoring.propose-form",
        "forms.authoring.propose-catalog", "forms.catalog-filters.metadata", "forms.catalog-filters.propose"];
    private static readonly string[] KnownTools = [..HomeQueries, ..HomeAuthoring, ..QualificationChannelContexts.Capabilities];

    private static Dictionary<string, string> Names(string es, string en) => new(StringComparer.Ordinal) { ["es"] = es, ["en"] = en };
    private static T[] Cases<T>(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", file)));
        return document.RootElement.GetProperty("cases").Deserialize<T[]>(JsonSerializerOptions.Web)!;
    }
    private static HomeCase[] HomeCases() => Cases<HomeCase>("home-authoring-cases.json");
    private static ChannelCase[] ChannelCases() => Cases<ChannelCase>("channel-authoring-cases.json");
    private static HomeCase Home(string id) => HomeCases().Single(testCase => testCase.Id == id);
    private static ChannelCase Channel(string id) => ChannelCases().Single(testCase => testCase.Id == id);
    private static string Stem(string id, string prefix) => id[prefix.Length..^3];

    // The shape a correct owner proposal has for the case: exact facts, a free name and a valid free prefix when none is requested.
    private static AssistantCapabilityPayload Ideal(AuthoringExpectation expected, string culture) => expected.Capability switch
    {
        "forms.authoring.propose-form" => new FormAuthoringFormProposal(culture, expected.Name ?? (culture == "es" ? "Arreglos de equipos" : "Equipment repairs"),
            expected.Prefix ?? IdealPrefix, culture == "es" ? "Enviar" : "Submit", expected.Questions!.Select(question => new FormAuthoringProposedQuestion(
                question.Label, question.Type, question.Required, question.Options ?? [],
                question.Catalog is { } alias ? new FormAuthoringProposedCatalog(Fixture.Catalog(alias).Id, Fixture.Catalog(alias).Names["es"]) : null)).ToArray()),
        "forms.authoring.propose-catalog" => new FormAuthoringCatalogProposal(culture, expected.Name!, culture == "es" ? "Catálogo ficticio." : "Fictitious catalog.",
            expected.ValueType!, Capitalized(expected.Code!), Capitalized(expected.Option!), expected.Fields!.Select(field => new FormAuthoringProposedField(field.Name, field.Type)).ToArray()),
        "forms.authoring.decline" => expected.DeclineReason switch
        {
            "formExists" => new FormAuthoringDecline("formExists", Fixture.Form(expected.ExistingForm!).DefinitionId, null, Fixture.Form(expected.ExistingForm!).Names["es"]),
            "catalogExists" => new FormAuthoringDecline("catalogExists", null, Fixture.Catalog(expected.ExistingCatalog!).Id, Fixture.Catalog(expected.ExistingCatalog!).Names["es"]),
            "unclear" => new FormAuthoringDecline("unclear", null, null, ""),
            _ => new FormAuthoringDecline("noPermission", null, null, ""),
        },
        "forms.catalog-filters.propose" => People(),
        _ => throw new InvalidOperationException("Offline authoring fixture has no ideal payload."),
    };
    private static string Capitalized(string value) => char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
    private static FormCatalogProposalPayload People(params FormCatalogFilterCondition[] extra) => new(1, "Supervisores activos", "Personas activas con el rol Supervisor.",
        new("name", null, [new("active", "equals", "true"), new("role", "has", Actors.SupervisorRoleId.ToString("D")), ..extra], false, "name", false), []);
    // The start an adjustment continues, by case Id across both public fixtures.
    private static Dictionary<string, AuthoringExpectation> Expectations() => HomeCases().Select(testCase => (testCase.Id, testCase.Authoring))
        .Concat(ChannelCases().Select(testCase => (testCase.Id, testCase.Authoring))).Where(item => item.Authoring is not null)
        .ToDictionary(item => item.Id, item => item.Authoring!, StringComparer.Ordinal);
    private static AssistantCapabilityPayload? Previous(AuthoringExpectation expected, string culture) =>
        expected.Continues is null ? null : Ideal(Expectations()[expected.Continues], culture);
    private static bool Passes(AuthoringExpectation expected, string culture, AssistantCapabilityPayload? payload, AssistantCapabilityPayload? previous = null)
    {
        var checks = AuthoringOracle.PayloadChecks(expected, culture, payload, Fixture, Actors, previous);
        return checks.Count > 0 && checks.All(check => check.Passed);
    }
    private static FormAuthoringFormProposal Form(string id) => (FormAuthoringFormProposal)Ideal(Home(id).Authoring!, Home(id).Culture);
    private static FormAuthoringProposedQuestion[] With(FormAuthoringFormProposal proposal, int index, FormAuthoringProposedQuestion question) =>
        proposal.Questions.Select((item, position) => position == index ? question : item).ToArray();

    public static void Authoring_fixtures_cover_the_stage_cases_in_both_cultures()
    {
        var home = HomeCases();var channel = ChannelCases();
        Require(home.Length == 24 && channel.Length == 22);
        Require(home.Select(testCase => testCase.Id).Concat(channel.Select(testCase => testCase.Id)).Distinct(StringComparer.Ordinal).Count() == 46);
        Require(home.All(testCase => Regex.IsMatch(testCase.Id, "^home-authoring-[a-z0-9-]+-(es|en)$", RegexOptions.CultureInvariant) && testCase.Id.EndsWith("-" + testCase.Culture, StringComparison.Ordinal)));
        Require(channel.All(testCase => Regex.IsMatch(testCase.Id, "^channel-authoring-[a-z0-9-]+-(es|en)$", RegexOptions.CultureInvariant) && testCase.Id.EndsWith("-" + testCase.Culture, StringComparison.Ordinal)));
        string[] homeStems = ["form", "catalog", "people-catalog", "adjust-start", "adjust-change", "corrected", "name-in-use", "no-permission", "ambiguous", "injection",
            "dictated-webm", "dictated-mp4"];
        string[] channelStems = ["form", "catalog", "adjust-start", "adjust-change", "corrected", "form-exists", "catalog-exists", "no-permission",
            "ambiguous-start", "ambiguous-answer", "injection"];
        foreach (var culture in new[] { "es", "en" })
        {
            Require(home.Where(testCase => testCase.Culture == culture).Select(testCase => Stem(testCase.Id, "home-authoring-")).Order(StringComparer.Ordinal).SequenceEqual(homeStems.Order(StringComparer.Ordinal)));
            Require(channel.Where(testCase => testCase.Culture == culture).Select(testCase => Stem(testCase.Id, "channel-authoring-")).Order(StringComparer.Ordinal).SequenceEqual(channelStems.Order(StringComparer.Ordinal)));
        }
        Require(home.All(testCase => testCase.Authoring is not null && testCase.ActorKind == (Stem(testCase.Id, "home-authoring-") == "no-permission" ? "home" : "author")
            && (testCase.Authoring.Capability is null ? testCase.ExpectedKind is "unsupported" or "clarification" && testCase.RequiredTools.Length == 0
                : testCase.ExpectedKind == "proposal" && testCase.RequiredTools.Contains(testCase.Authoring.Capability, StringComparer.Ordinal))));
        Require(home.Where(testCase => Stem(testCase.Id, "home-authoring-") == "no-permission").All(testCase => testCase.ExpectedKind == "unsupported")
            && home.Where(testCase => Stem(testCase.Id, "home-authoring-") == "ambiguous").All(testCase => testCase.ExpectedKind == "clarification"));
        Require(channel.All(testCase => testCase.Authoring?.Capability is { } capability && testCase.RequiredTools.Contains(capability, StringComparer.Ordinal)
            && testCase.ActorKind == (Stem(testCase.Id, "channel-authoring-") == "no-permission" ? "auto" : "author")
            && testCase.ExpectedPayload == capability switch
            {
                "forms.authoring.propose-form" => "authoring-form", "forms.authoring.propose-catalog" => "authoring-catalog",
                "forms.authoring.decline" => "authoring-decline", _ => "unknown",
            }));
        // An adjustment continues the start of its own culture and actor, in the same Home conversation or after it in the channel.
        foreach (var change in home.Where(testCase => testCase.Authoring!.Continues is not null))
        {
            var start = home.Single(testCase => testCase.Id == change.Authoring!.Continues);
            Require(Array.IndexOf(home, start) < Array.IndexOf(home, change) && start.ConversationKey is { } key && change.ConversationKey == key
                && start.Culture == change.Culture && start.ActorKind == change.ActorKind && start.Authoring!.Name == change.Authoring!.Name);
        }
        // In the channel the start is right before; an answer to «what should I build?» names the form for the first time.
        foreach (var change in channel.Where(testCase => testCase.Authoring!.Continues is not null))
        {
            var start = channel.Single(testCase => testCase.Id == change.Authoring!.Continues);
            Require(Array.IndexOf(channel, start) == Array.IndexOf(channel, change) - 1 && start.Culture == change.Culture
                && start.ActorKind == change.ActorKind && (start.Authoring!.DeclineReason == "unclear" ? change.Authoring!.Name is not null
                    : start.Authoring!.Name == change.Authoring!.Name));
        }
        Require(channel.Where(testCase => Stem(testCase.Id, "channel-authoring-") == "ambiguous-start")
            .All(testCase => testCase.Authoring is { Capability: "forms.authoring.decline", DeclineReason: "unclear" })
            && channel.Where(testCase => Stem(testCase.Id, "channel-authoring-") == "ambiguous-answer")
                .All(testCase => testCase.Authoring!.Continues == "channel-authoring-ambiguous-start-" + testCase.Culture));
    }

    public static void Authoring_fixtures_stay_fictitious_and_generic()
    {
        // Only generic fictitious business facts: no real company, address, link, email or phone-like number in what is said or expected.
        var texts = HomeCases().Select(testCase => (testCase.Question, Spoken: testCase.Audio?.ExpectedTranscript, testCase.Authoring))
            .Concat(ChannelCases().Select(testCase => (testCase.Question, Spoken: (string?)null, testCase.Authoring)))
            .Select(item => item.Question + "\n" + item.Spoken + "\n" + JsonSerializer.Serialize(item.Authoring, JsonSerializerOptions.Web)).ToArray();
        Require(texts.Length == 46 && texts.All(text => !Regex.IsMatch(text, @"macrosistemas|\bocha\b|https?:|www\.|[\w.+-]+@[\w-]+\.\w", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            && !Regex.IsMatch(text, @"\d[\d\s().-]{5,}\d", RegexOptions.CultureInvariant)));
        // A typed case says something; a dictated one says it only through its recording.
        Require(HomeCases().All(testCase => testCase.Audio is null ? testCase.Question.Length is > 0 and <= 4000 : testCase.Question.Length == 0)
            && ChannelCases().All(testCase => testCase.Question.Length is > 0 and <= 4000));
    }

    public static void Authoring_fixtures_reference_known_capabilities_and_aliases()
    {
        var expectations = HomeCases().Select(testCase => (testCase.RequiredTools, testCase.Authoring!))
            .Concat(ChannelCases().Select(testCase => (testCase.RequiredTools, testCase.Authoring!))).ToArray();
        Require(expectations.All(item => item.RequiredTools.All(tool => KnownTools.Contains(tool, StringComparer.Ordinal))));
        var forms = QualificationFormSeed.FormNames;var catalogs = QualificationFormSeed.CatalogNames;
        foreach (var (_, expected) in expectations)
        {
            Require(expected.Questions?.All(question => question.Catalog is null || catalogs.Any(catalog => catalog.Alias == question.Catalog)) != false);
            Require(expected.ExistingForm is null || forms.Any(form => form.Alias == expected.ExistingForm));
            Require(expected.ExistingCatalog is null || catalogs.Any(catalog => catalog.Alias == expected.ExistingCatalog));
            Require(expected.Prefix is null || Regex.IsMatch(expected.Prefix, "^[A-Z][A-Z0-9]{0,11}$", RegexOptions.CultureInvariant)
                && forms.All(form => form.Prefix != expected.Prefix));
            // A requested name never collides with the seeded fixture: only the name-in-use case leaves the name to the model.
            Require(expected.Name is null || forms.All(form => !Same(form.Spanish, expected.Name) && !Same(form.English, expected.Name))
                && catalogs.All(catalog => !Same(catalog.Spanish, expected.Name) && !Same(catalog.English, expected.Name)));
        }
    }
    private static bool Same(string left, string right) => AuthoringOracle.Canonical(left) == AuthoringOracle.Canonical(right);

    public static void Authoring_ideal_proposals_pass_every_payload_check()
    {
        foreach (var testCase in HomeCases().Where(item => item.Authoring!.Capability is not null))
            Require(Passes(testCase.Authoring!, testCase.Culture, Ideal(testCase.Authoring!, testCase.Culture), Previous(testCase.Authoring!, testCase.Culture)));
        foreach (var testCase in ChannelCases())
            Require(Passes(testCase.Authoring!, testCase.Culture, Ideal(testCase.Authoring!, testCase.Culture), Previous(testCase.Authoring!, testCase.Culture)));
        foreach (var testCase in HomeCases().Where(item => item.Authoring!.Capability is null))
            Require(AuthoringOracle.PayloadChecks(testCase.Authoring!, testCase.Culture, null, Fixture, Actors, null).Count == 0);
    }

    public static void Authoring_canonical_text_ignores_only_case_accents_and_punctuation()
    {
        Require(AuthoringOracle.Canonical("«Fecha de Entrega»!") == "fecha de entrega" && AuthoringOracle.Canonical("¿Mañana?") == AuthoringOracle.Canonical("manana"));
        Require(AuthoringOracle.Canonical("Fecha entrega") != AuthoringOracle.Canonical("Fecha de entrega")
            && AuthoringOracle.Canonical("Requiere calibración") == "requiere calibracion" && AuthoringOracle.Canonical(" \t") == "");
        var proposal = Form("home-authoring-form-es");
        Require(Passes(Home("home-authoring-form-es").Authoring!, "es", proposal with { Name = "entregas de MERCADERIA",
            Questions = With(proposal, 0, proposal.Questions[0] with { Label = "¿Cliente?" }) }));
    }

    public static void Authoring_form_rejects_an_extra_missing_or_reordered_question()
    {
        var expected = Home("home-authoring-form-es").Authoring!;var proposal = Form("home-authoring-form-es");
        Require(Passes(expected, "es", proposal));
        Require(!Passes(expected, "es", proposal with { Questions = [..proposal.Questions, new("Contraseña del sistema", "shortText", false, [], null)] }));
        Require(!Passes(expected, "es", proposal with { Questions = proposal.Questions.Take(proposal.Questions.Count - 1).ToArray() }));
        Require(!Passes(expected, "es", proposal with { Questions = [proposal.Questions[1], proposal.Questions[0], ..proposal.Questions.Skip(2)] }));
        Require(!Passes(expected, "es", proposal with { Questions = [] }));
    }

    public static void Authoring_form_rejects_a_changed_type_requirement_or_option()
    {
        var expected = Home("home-authoring-form-es").Authoring!;var proposal = Form("home-authoring-form-es");
        Require(!Passes(expected, "es", proposal with { Questions = With(proposal, 0, proposal.Questions[0] with { Type = "longText" }) }));
        Require(!Passes(expected, "es", proposal with { Questions = With(proposal, 2, proposal.Questions[2] with { Required = true }) }));
        Require(!Passes(expected, "es", proposal with { Questions = With(proposal, 0, proposal.Questions[0] with { Label = "Nombre del cliente" }) }));
        Require(!Passes(expected, "es", proposal with { Questions = With(proposal, 3, proposal.Questions[3] with { Options = ["Mañana"] }) }));
        Require(!Passes(expected, "es", proposal with { Questions = With(proposal, 3, proposal.Questions[3] with { Options = ["Mañana", "Tarde", "Noche"] }) }));
        Require(!Passes(expected, "es", proposal with { Questions = With(proposal, 3, proposal.Questions[3] with { Options = ["Tarde", "Mañana"] }) }));
        Require(!Passes(expected, "es", proposal with { Questions = With(proposal, 3, proposal.Questions[3] with { Type = "multiChoice" }) }));
        Require(!Passes(expected, "es", proposal with { Questions = With(proposal, 0, proposal.Questions[0] with { Options = ["Interno"] }) }));
    }

    public static void Authoring_form_requires_the_existing_catalog_by_identity()
    {
        var expected = Home("home-authoring-form-es").Authoring!;var proposal = Form("home-authoring-form-es");
        var index = expected.Questions!.ToList().FindIndex(question => question.Type == "catalog");
        var question = proposal.Questions[index];
        Require(!Passes(expected, "es", proposal with { Questions = With(proposal, index, question with { Catalog = question.Catalog! with { Id = Fixture.Catalog("branches").Id } }) }));
        Require(!Passes(expected, "es", proposal with { Questions = With(proposal, index, question with { Catalog = question.Catalog! with { Id = Guid.NewGuid() } }) }));
        Require(!Passes(expected, "es", proposal with { Questions = With(proposal, index, question with { Catalog = null }) }));
        Require(!Passes(expected, "es", proposal with { Questions = With(proposal, index, question with { Type = "choice", Catalog = null, Options = ["Rojo", "Azul"] }) }));
        Require(!Passes(expected, "es", proposal with { Questions = With(proposal, 0, proposal.Questions[0] with { Catalog = question.Catalog }) }));
        // The catalog's displayed name is the owner's own: only its identity is the observed fact.
        Require(Passes(expected, "es", proposal with { Questions = With(proposal, index, question with { Catalog = question.Catalog! with { Name = "Colores" } }) }));
    }

    public static void Authoring_form_rejects_a_name_or_prefix_already_in_use()
    {
        foreach (var culture in new[] { "es", "en" })
        {
            var testCase = Home("home-authoring-name-in-use-" + culture);var proposal = (FormAuthoringFormProposal)Ideal(testCase.Authoring!, culture);
            Require(Passes(testCase.Authoring!, culture, proposal));
            foreach (var text in QualificationFormSeed.FormNames)
            {
                Require(!Passes(testCase.Authoring!, culture, proposal with { Name = text.Spanish }));
                Require(!Passes(testCase.Authoring!, culture, proposal with { Name = text.English.ToUpperInvariant() + "." }));
                Require(!Passes(testCase.Authoring!, culture, proposal with { Prefix = text.Prefix }));
            }
            Require(!Passes(testCase.Authoring!, culture, proposal with { Name = " " }));
            Require(!Passes(testCase.Authoring!, culture, proposal with { Prefix = "MANT-26" }));
            Require(!Passes(testCase.Authoring!, culture, proposal with { Prefix = "2026MANT" }));
            Require(!Passes(testCase.Authoring!, culture, proposal with { Prefix = "mant" }));
            Require(!Passes(testCase.Authoring!, culture, proposal with { Prefix = "MANTENIMIENTO1" }));
        }
        var catalog = Home("home-authoring-catalog-es");var proposed = (FormAuthoringCatalogProposal)Ideal(catalog.Authoring!, "es");
        Require(!Passes(catalog.Authoring!, "es", proposed with { Name = "Color" }) && !Passes(catalog.Authoring! with { Name = null }, "es", proposed with { Name = "Branch" }));
    }

    public static void Authoring_form_keeps_the_requested_name_prefix_and_culture()
    {
        foreach (var culture in new[] { "es", "en" })
        {
            var testCase = Home("home-authoring-corrected-" + culture);var proposal = (FormAuthoringFormProposal)Ideal(testCase.Authoring!, culture);
            Require(Passes(testCase.Authoring!, culture, proposal) && proposal.Prefix == testCase.Authoring!.Prefix);
            Require(!Passes(testCase.Authoring!, culture, proposal with { Prefix = proposal.Prefix[..3] }));
            Require(!Passes(testCase.Authoring!, culture, proposal with { Prefix = proposal.Prefix[..3] + "-" + proposal.Prefix[3..] }));
            Require(!Passes(testCase.Authoring!, culture, proposal with { Name = proposal.Name + " nuevos" }));
            Require(!Passes(testCase.Authoring!, culture, proposal with { Culture = culture == "es" ? "en" : "es" }));
        }
    }

    // An adjustment continues a shown form; the answer to «what should I build?» has its own check.
    public static void Authoring_adjustment_keeps_the_previous_name_and_prefix()
    {
        foreach (var testCase in HomeCases().Where(item => item.Authoring!.Continues is not null).Select(item => (item.Authoring!, item.Culture))
            .Concat(ChannelCases().Where(item => item.Authoring!.Continues is not null).Select(item => (item.Authoring!, item.Culture)))
            .Where(item => Expectations()[item.Item1.Continues!].Capability == "forms.authoring.propose-form"))
        {
            var (expected, culture) = testCase;
            var previous = (FormAuthoringFormProposal)Ideal(Expectations()[expected.Continues!], culture);
            var proposal = (FormAuthoringFormProposal)Ideal(expected, culture);
            Require(Passes(expected, culture, proposal, previous));
            Require(!Passes(expected, culture, proposal, null));
            Require(!Passes(expected, culture, proposal, previous with { Prefix = "OTRO" }));
            Require(!Passes(expected, culture, proposal with { Prefix = "OTRO" }, previous));
            // The unchanged start is not the adjusted proposal: the requested change must be there.
            Require(!Passes(expected, culture, previous, previous));
        }
    }

    public static void Authoring_catalog_rejects_wrong_value_type_code_option_or_fields()
    {
        foreach (var testCase in HomeCases().Where(item => item.Authoring!.Capability == "forms.authoring.propose-catalog").Select(item => (item.Authoring!, item.Culture))
            .Concat(ChannelCases().Where(item => item.Authoring!.Capability == "forms.authoring.propose-catalog").Select(item => (item.Authoring!, item.Culture))))
        {
            var (expected, culture) = testCase;var proposal = (FormAuthoringCatalogProposal)Ideal(expected, culture);
            Require(Passes(expected, culture, proposal));
            Require(!Passes(expected, culture, proposal with { ValueType = "integer" }));
            Require(!Passes(expected, culture, proposal with { Code = proposal.Option }));
            Require(!Passes(expected, culture, proposal with { Option = "" }));
            Require(!Passes(expected, culture, proposal with { Fields = proposal.Fields.Reverse().ToArray() }));
            Require(!Passes(expected, culture, proposal with { Fields = [..proposal.Fields, new("Notas", "text")] }));
            Require(!Passes(expected, culture, proposal with { Fields = proposal.Fields.Skip(1).ToArray() }));
            Require(!Passes(expected, culture, proposal with { Fields = proposal.Fields.Select((field, index) => index == 0 ? field with { Type = "date" } : field).ToArray() }));
            Require(!Passes(expected, culture, proposal with { Culture = culture == "es" ? "en" : "es" }));
            Require(!Passes(expected, culture, proposal with { Name = proposal.Name + " 2" }));
            Require(Passes(expected, culture, proposal with { Code = proposal.Code + " " + (culture == "es" ? "de cada uno" : "of each one") }));
        }
    }

    public static void Authoring_decline_requires_the_checked_reason_and_target()
    {
        foreach (var culture in new[] { "es", "en" })
        {
            var form = Channel("channel-authoring-form-exists-" + culture).Authoring!;var catalog = Channel("channel-authoring-catalog-exists-" + culture).Authoring!;
            var none = Channel("channel-authoring-no-permission-" + culture).Authoring!;
            var formDecline = (FormAuthoringDecline)Ideal(form, culture);var catalogDecline = (FormAuthoringDecline)Ideal(catalog, culture);
            var noneDecline = (FormAuthoringDecline)Ideal(none, culture);
            Require(Passes(form, culture, formDecline) && Passes(catalog, culture, catalogDecline) && Passes(none, culture, noneDecline));
            Require(!Passes(form, culture, formDecline with { DefinitionId = Fixture.Form("maintenance").DefinitionId }));
            Require(!Passes(form, culture, formDecline with { CatalogId = Fixture.Catalog("colors").Id }));
            Require(!Passes(form, culture, formDecline with { Name = "" }));
            Require(!Passes(catalog, culture, catalogDecline with { CatalogId = Fixture.Catalog("branches").Id }));
            Require(!Passes(catalog, culture, catalogDecline with { DefinitionId = Fixture.Form("visit").DefinitionId }));
            Require(!Passes(none, culture, noneDecline with { DefinitionId = Fixture.Form("visit").DefinitionId }));
            Require(!Passes(none, culture, noneDecline with { Name = "Registrar visita" }));
            Require(!Passes(none, culture, formDecline) && !Passes(form, culture, noneDecline) && !Passes(form, culture, catalogDecline));
            var unclear = Channel("channel-authoring-ambiguous-start-" + culture).Authoring!;var unclearDecline = (FormAuthoringDecline)Ideal(unclear, culture);
            Require(Passes(unclear, culture, unclearDecline) && !Passes(unclear, culture, noneDecline) && !Passes(none, culture, unclearDecline));
            Require(!Passes(unclear, culture, unclearDecline with { Name = "Registrar visita" }) && !Passes(unclear, culture, formDecline));
        }
    }

    public static void Authoring_people_catalog_keeps_the_exact_supervisor_filters()
    {
        var expected = Home("home-authoring-people-catalog-es").Authoring!;
        Require(Passes(expected, "es", People()));
        Require(!Passes(expected, "es", People(new FormCatalogFilterCondition("name", "contains", "Ana"))));
        var people = People();
        Require(!Passes(expected, "es", people with { Filters = people.Filters with { ExcludeRecipient = true } }));
        Require(!Passes(expected, "es", people with { Filters = people.Filters with { Conditions = [new("active", "equals", "true")] } }));
        Require(!Passes(expected, "es", people with { Filters = people.Filters with { Conditions = [new("active", "equals", "true"), new("role", "has", Guid.NewGuid().ToString("D"))] } }));
    }

    public static void Authoring_payload_of_another_owner_fails_every_stable_check()
    {
        foreach (var testCase in HomeCases().Where(item => item.Authoring!.Capability is not null))
        {
            var ideal = AuthoringOracle.PayloadChecks(testCase.Authoring!, testCase.Culture, Ideal(testCase.Authoring!, testCase.Culture), Fixture, Actors,
                Previous(testCase.Authoring!, testCase.Culture));
            foreach (AssistantCapabilityPayload? other in new AssistantCapabilityPayload?[] { null, new WhatsAppAssistantNoReplyPayload("not_form"),
                new FormAuthoringRejection([new("prefix", "invalid")]) })
            {
                var checks = AuthoringOracle.PayloadChecks(testCase.Authoring!, testCase.Culture, other, Fixture, Actors, null);
                Require(checks.Select(check => check.Id).SequenceEqual(ideal.Select(check => check.Id)) && checks.Count > 0 && checks.Any(check => !check.Passed)
                    && checks.Single(check => check.Id == "authoring-payload-type") is { Passed: false });
            }
        }
    }

    private static AssistantObservedProposal Observed(Guid turn, string capability, AssistantCapabilityPayload payload, params AssistantObservedRead[] reads) =>
        new(turn, Guid.NewGuid(), new(capability, 1), payload) { ObservedReads = reads };
    private static AssistantObservedRead FormsRead(Guid turn, int ordinal = 1) => new(turn, ordinal, new("forms.authoring.forms", 1), new FormAuthoringFormsPayload(true, []));
    private static AssistantObservedRead CatalogsRead(Guid turn, int ordinal = 2) => new(turn, ordinal, new("forms.authoring.catalogs", 1), new FormAuthoringCatalogsPayload(true, []));

    public static void Authoring_reads_must_precede_the_proposal_in_the_same_turn()
    {
        var turn = Guid.NewGuid();var payload = Form("home-authoring-form-es");
        string[] required = ["forms.authoring.forms", "forms.authoring.catalogs", "forms.authoring.propose-form"];
        Require(AuthoringOracle.ReadsBeforeProposal(Observed(turn, "forms.authoring.propose-form", payload, FormsRead(turn), CatalogsRead(turn)), required));
        Require(!AuthoringOracle.ReadsBeforeProposal(Observed(turn, "forms.authoring.propose-form", payload, FormsRead(turn)), required));
        Require(!AuthoringOracle.ReadsBeforeProposal(Observed(turn, "forms.authoring.propose-form", payload, FormsRead(Guid.NewGuid()), CatalogsRead(turn)), required));
        Require(!AuthoringOracle.ReadsBeforeProposal(Observed(turn, "forms.authoring.propose-form", payload, FormsRead(turn, 0), CatalogsRead(turn)), required));
        Require(!AuthoringOracle.ReadsBeforeProposal(Observed(turn, "forms.authoring.propose-form", payload,
            new AssistantObservedRead(turn, 1, new("forms.authoring.forms", 1), new FormAuthoringCatalogsPayload(true, [])), CatalogsRead(turn)), required));
        Require(!AuthoringOracle.ReadsBeforeProposal(Observed(turn, "forms.authoring.propose-form", payload,
            new AssistantObservedRead(turn, 1, new("forms.authoring.forms", 2), new FormAuthoringFormsPayload(true, [])), CatalogsRead(turn)), required));
        Require(!AuthoringOracle.ReadsBeforeProposal(null, required));
        Require(AuthoringOracle.ReadsBeforeProposal(Observed(turn, "forms.authoring.propose-form", payload), ["forms.authoring.propose-form"]));
        Require(AuthoringOracle.ReadsBeforeProposal(Observed(turn, "forms.catalog-filters.propose", People(),
            new AssistantObservedRead(turn, 1, new("forms.catalog-filters.metadata", 1), new FormCatalogMetadataPayload("users", []))),
            ["forms.catalog-filters.metadata", "forms.catalog-filters.propose"]));
    }

    private static ObservedCapability Tool(string id, string state) => new(id, 1, 128, state);

    public static void Authoring_rejection_must_be_followed_by_an_accepted_proposal()
    {
        const string form = "forms.authoring.propose-form";
        Require(AuthoringOracle.RejectionsRecovered([], form) && AuthoringOracle.RejectionsRecovered([Tool("forms.authoring.forms", "complete"), Tool(form, "proposed")], form));
        Require(AuthoringOracle.RejectionsRecovered([Tool("forms.authoring.forms", "complete"), Tool(form, "rejected"), Tool(form, "proposed")], form));
        Require(AuthoringOracle.RejectionsRecovered([Tool(form, "rejected"), Tool(form, "rejected"), Tool(form, "proposed")], form));
        Require(!AuthoringOracle.RejectionsRecovered([Tool(form, "rejected")], form));
        Require(!AuthoringOracle.RejectionsRecovered([Tool(form, "proposed"), Tool(form, "rejected")], form));
        Require(!AuthoringOracle.RejectionsRecovered([Tool(form, "rejected"), Tool("forms.authoring.propose-catalog", "proposed")], form));
        Require(AuthoringOracle.RejectionsRecovered([Tool("forms.authoring.decline", "rejected"), Tool("forms.authoring.decline", "proposed")], "forms.authoring.decline"));
    }

    public static void Authoring_turn_proposes_only_the_expected_capability()
    {
        const string form = "forms.authoring.propose-form";
        Require(AuthoringOracle.OnlyProposalsOf([Tool("forms.authoring.forms", "complete"), Tool(form, "rejected"), Tool(form, "proposed")], ProposalIds, form));
        Require(!AuthoringOracle.OnlyProposalsOf([Tool(form, "proposed"), Tool("forms.catalog-filters.propose", "proposed")], ProposalIds, form));
        Require(!AuthoringOracle.OnlyProposalsOf([Tool(form, "proposed"), Tool("forms.authoring.propose-catalog", "rejected")], ProposalIds, form));
        Require(!AuthoringOracle.OnlyProposalsOf([Tool("forms.authoring.forms", "complete")], ProposalIds, form));
        Require(AuthoringOracle.OnlyProposalsOf([Tool("forms.authoring.forms", "complete"), Tool("users.summary", "complete")], ProposalIds, null));
        Require(!AuthoringOracle.OnlyProposalsOf([Tool(form, "rejected")], ProposalIds, null));
        Require(AuthoringOracle.OnlyProposalsOf([], ProposalIds, null));
    }

    public static void Authoring_offer_follows_the_account_permissions()
    {
        string[] author = [..HomeQueries, ..HomeAuthoring];
        Require(AuthoringOracle.Offered(author, HomeAuthoring, ["forms.authoring.decline"]));
        Require(!AuthoringOracle.Offered(author.Where(id => id != "forms.authoring.propose-catalog").ToArray(), HomeAuthoring, []));
        Require(!AuthoringOracle.Offered([..author, "forms.authoring.decline"], HomeAuthoring, ["forms.authoring.decline"]));
        Require(AuthoringOracle.Offered(HomeQueries, [], HomeAuthoring) && !AuthoringOracle.Offered([..HomeQueries, "forms.authoring.forms"], [], HomeAuthoring));
    }

    private static readonly string[] HomeOffered = [..HomeQueries, ..HomeAuthoring];
    private static (AiAssistantTurnResponse Result, TurnMetadata Technical) HomeTurn(HomeCase testCase, Guid conversation, int ordinal,
        IReadOnlyList<string>? offered = null, string kind = "proposal", int instructionsVersion = 2, Guid? proposalId = null)
    {
        var turn = Guid.NewGuid();var payload = testCase.Authoring!.Capability is null ? null : Ideal(testCase.Authoring!, testCase.Culture);
        var reads = testCase.RequiredTools.Select((tool, index) => tool switch
        {
            "forms.authoring.forms" => FormsRead(turn, index + 1),
            "forms.authoring.catalogs" => CatalogsRead(turn, index + 1),
            "forms.catalog-filters.metadata" => new AssistantObservedRead(turn, index + 1, new(tool, 1), new FormCatalogMetadataPayload("users", [])),
            _ => null,
        }).OfType<AssistantObservedRead>().ToArray();
        var observed = payload is null ? null : Observed(turn, testCase.Authoring!.Capability!, payload, reads);
        var result = new AiAssistantTurnResponse(turn, conversation, Guid.NewGuid(), ordinal, ordinal, 1, "completed", "completed", "allowed",
            "public fixture", new AiAssistantAnswer(payload is null ? testCase.ExpectedKind : kind, "Texto ficticio.", []), [], null, null,
            testCase.Culture, "America/Argentina/Buenos_Aires", DateTime.UnixEpoch, DateTime.UnixEpoch, 2)
        {
            ObservedProposal = observed,
            Proposal = observed is null ? null : new AiAssistantProposalResponse(proposalId ?? observed.Id, observed.Capability.Id, 1, JsonSerializer.SerializeToElement(new { })),
        };
        var tools = testCase.RequiredTools.Select(tool => Tool(tool, ProposalIds.Contains(tool) ? "proposed" : "complete")).ToArray();
        var technical = new TurnMetadata(3, tools.Length, 512, null, null, true, null,
            new("gpt-6.1-sol-candidate-1", 1, "public", "home-system", instructionsVersion, "home"), tools)
        { ConversationId = conversation, Ordinal = ordinal, OfferedCapabilities = offered ?? (testCase.ActorKind == "author" ? HomeOffered : HomeQueries) };
        return (result, technical);
    }
    private static bool HomeAccepted(HomeCase testCase, AiAssistantTurnResponse result, TurnMetadata technical,
        (Guid Id, long Revision)? conversation = null, AssistantObservedProposal? previous = null)
    {
        var checks = new List<CheckEvidence>();
        HomeRunner.AddAuthoringChecks(checks, testCase, result, technical, Fixture, Actors, ProposalIds, conversation, previous);
        return checks.Count > 0 && checks.All(check => check.Passed) && checks.Select(check => check.Id).Distinct(StringComparer.Ordinal).Count() == checks.Count;
    }

    public static void Home_authoring_checks_accept_the_observed_proposal_turn()
    {
        foreach (var testCase in HomeCases().Where(item => item.Authoring!.Capability is not null && item.Authoring.Continues is null))
        {
            var (result, technical) = HomeTurn(testCase, Guid.NewGuid(), 1);
            Require(HomeAccepted(testCase, result, technical));
        }
    }

    public static void Home_authoring_checks_reject_a_mismatched_terminal_or_stamp()
    {
        var testCase = Home("home-authoring-form-es");
        var (result, technical) = HomeTurn(testCase, Guid.NewGuid(), 1);
        Require(!HomeAccepted(testCase, result with { Answer = result.Answer! with { Kind = "answer" } }, technical));
        Require(!HomeAccepted(testCase, result with { Proposal = result.Proposal! with { Id = Guid.NewGuid() } }, technical));
        Require(!HomeAccepted(testCase, result with { ObservedProposal = result.ObservedProposal! with { TurnId = Guid.NewGuid() } }, technical));
        Require(!HomeAccepted(testCase, result with { ObservedProposal = result.ObservedProposal! with { Capability = new("forms.authoring.propose-catalog", 1) } }, technical));
        Require(!HomeAccepted(testCase, result, technical with { Stamps = technical.Stamps with { InstructionsVersion = 1 } }));
        Require(!HomeAccepted(testCase, result, technical with { OfferedCapabilities = HomeQueries }));
        Require(!HomeAccepted(testCase, result, technical with { Capabilities = [..technical.Capabilities, Tool("forms.authoring.propose-form", "rejected")] }));
        Require(!HomeAccepted(testCase, result, technical with { Capabilities = [..technical.Capabilities, Tool("forms.catalog-filters.propose", "proposed")] }));
        Require(!HomeAccepted(testCase, result with { ObservedProposal = result.ObservedProposal! with { ObservedReads = [] } }, technical));
        Require(HomeAccepted(testCase, result, technical with { Capabilities = [technical.Capabilities[0], technical.Capabilities[1],
            Tool("forms.authoring.propose-form", "rejected"), technical.Capabilities[2]] }));
    }

    public static void Home_authoring_checks_require_no_proposal_without_permission_or_details()
    {
        foreach (var testCase in HomeCases().Where(item => item.Authoring!.Capability is null))
        {
            var (result, technical) = HomeTurn(testCase, Guid.NewGuid(), 1);
            Require(HomeAccepted(testCase, result, technical));
            Require(!HomeAccepted(testCase, result, technical with { Capabilities = [Tool("forms.authoring.propose-form", "rejected")] }));
            var proposal = Observed(result.TurnId, "forms.authoring.propose-form", Form("home-authoring-form-es"));
            Require(!HomeAccepted(testCase, result with { ObservedProposal = proposal }, technical));
            if (testCase.ActorKind == "home")
                Require(!HomeAccepted(testCase, result, technical with { OfferedCapabilities = HomeOffered }));
            else Require(!HomeAccepted(testCase, result, technical with { OfferedCapabilities = HomeQueries }));
        }
    }

    public static void Home_authoring_adjustment_requires_the_same_conversation()
    {
        foreach (var culture in new[] { "es", "en" })
        {
            var start = Home("home-authoring-adjust-start-" + culture);var change = Home("home-authoring-adjust-change-" + culture);
            var conversation = Guid.NewGuid();
            var (first, _) = HomeTurn(start, conversation, 1);
            var (result, technical) = HomeTurn(change, conversation, 2);
            Require(HomeAccepted(change, result, technical, (conversation, 1), first.ObservedProposal));
            Require(!HomeAccepted(change, result, technical, (Guid.NewGuid(), 1), first.ObservedProposal));
            Require(!HomeAccepted(change, result, technical, (conversation, 1), null));
            Require(!HomeAccepted(change, result, technical, null, first.ObservedProposal));
        }
    }

    private static readonly string[] AutomaticOffered = [..QualificationChannelContexts.Capabilities.Where(id => id is not ("forms.suggestion-candidates" or "forms.propose-suggestion"))];
    private static (AssistantOperationResponse Result, TurnMetadata Technical) ChannelTurn(ChannelCase testCase, Guid conversation, int ordinal,
        IReadOnlyList<string>? offered = null, AssistantCapabilityPayload? payload = null)
    {
        var turn = Guid.NewGuid();payload ??= Ideal(testCase.Authoring!, testCase.Culture);
        var reads = testCase.RequiredTools.Select((tool, index) => tool switch
        {
            "forms.authoring.forms" => FormsRead(turn, index + 1),
            "forms.authoring.catalogs" => CatalogsRead(turn, index + 1),
            _ => null,
        }).OfType<AssistantObservedRead>().ToArray();
        var observed = Observed(turn, testCase.Authoring!.Capability!, payload, reads);
        var result = new AssistantOperationResponse(Guid.NewGuid(), "completed", observed,
            new("gpt-6.1-sol-candidate-1", 1, "capabilities-v1", 1, DateTime.UnixEpoch, "whatsapp-assistant"), null);
        var tools = testCase.RequiredTools.Select(tool => Tool(tool, ProposalIds.Contains(tool) ? "proposed" : "complete")).ToArray();
        var technical = new TurnMetadata(3, tools.Length, 512, null, null, true, null,
            new("gpt-6.1-sol-candidate-1", 1, "public", "capabilities-v1", 1, "whatsapp-assistant"), tools)
        {
            ConversationId = conversation, Ordinal = ordinal,
            OfferedCapabilities = offered ?? (testCase.ActorKind == "author" ? AutomaticOffered
                : AutomaticOffered.Where(id => !id.StartsWith("forms.authoring.", StringComparison.Ordinal) && !id.StartsWith("forms.catalog-filters.", StringComparison.Ordinal))
                    .Append("forms.authoring.decline").ToArray()),
        };
        return (result, technical);
    }
    private static bool ChannelAccepted(ChannelCase testCase, AssistantOperationResponse result, TurnMetadata technical, ChannelObservation? previous = null)
    {
        var checks = new List<CheckEvidence>();
        ChannelRunner.AddAuthoringChecks(checks, testCase, result, technical, Fixture, Actors, ProposalIds, previous);
        return checks.Count > 0 && checks.All(check => check.Passed) && checks.Select(check => check.Id).Distinct(StringComparer.Ordinal).Count() == checks.Count;
    }

    public static void Channel_authoring_checks_accept_the_observed_proposal_and_decline()
    {
        foreach (var testCase in ChannelCases().Where(item => item.Authoring!.Continues is null))
        {
            var (result, technical) = ChannelTurn(testCase, Guid.NewGuid(), 1);
            Require(ChannelAccepted(testCase, result, technical));
        }
    }

    public static void Channel_authoring_checks_reject_wrong_offer_or_reason()
    {
        var author = Channel("channel-authoring-form-es");var (result, technical) = ChannelTurn(author, Guid.NewGuid(), 1);
        Require(!ChannelAccepted(author, result, technical with { OfferedCapabilities = technical.OfferedCapabilities.Where(id => id != "forms.authoring.decline").ToArray() }));
        Require(!ChannelAccepted(author, result with { Proposal = null }, technical));
        Require(!ChannelAccepted(author, result, technical with { Capabilities = [..technical.Capabilities, Tool("forms.missing-request.propose", "proposed")] }));
        var none = Channel("channel-authoring-no-permission-es");var (declined, declinedTechnical) = ChannelTurn(none, Guid.NewGuid(), 1);
        Require(ChannelAccepted(none, declined, declinedTechnical));
        Require(!ChannelAccepted(none, declined, declinedTechnical with { OfferedCapabilities = AutomaticOffered }));
        Require(!ChannelAccepted(none, declined, declinedTechnical with { OfferedCapabilities = declinedTechnical.OfferedCapabilities.Where(id => id != "forms.authoring.decline").ToArray() }));
        var (wrong, wrongTechnical) = ChannelTurn(none, Guid.NewGuid(), 1, payload: new FormAuthoringDecline("formExists", Fixture.Form("visit").DefinitionId, null, "Registrar visita"));
        Require(!ChannelAccepted(none, wrong, wrongTechnical));
        var exists = Channel("channel-authoring-form-exists-en");var (existing, existingTechnical) = ChannelTurn(exists, Guid.NewGuid(), 1);
        Require(!ChannelAccepted(exists, existing with { Proposal = existing.Proposal! with { ObservedReads = [] } }, existingTechnical));
    }

    public static void Channel_authoring_continuation_requires_the_next_turn_of_the_same_conversation()
    {
        foreach (var culture in new[] { "es", "en" })
        {
            var start = Channel("channel-authoring-adjust-start-" + culture);var change = Channel("channel-authoring-adjust-change-" + culture);
            var conversation = Guid.NewGuid();
            var (first, firstTechnical) = ChannelTurn(start, conversation, 1);
            var previous = new ChannelObservation(first.RequestId, first, firstTechnical, 1, [], new(null, null));
            var (result, technical) = ChannelTurn(change, conversation, 2);
            Require(ChannelAccepted(change, result, technical, previous));
            Require(!ChannelAccepted(change, result, technical, null));
            Require(!ChannelAccepted(change, result, technical with { ConversationId = Guid.NewGuid() }, previous));
            Require(!ChannelAccepted(change, result, technical with { Ordinal = 3 }, previous));
            Require(!ChannelAccepted(change, result, technical, previous with { Result = first with { Proposal = first.Proposal! with { Payload = Form("home-authoring-form-es") } } }));
        }
    }

    // «Pregunta y espera»: the answer is the next turn of the conversation whose last proposal asked what to build.
    public static void Channel_authoring_answer_follows_the_question_of_the_same_conversation()
    {
        foreach (var culture in new[] { "es", "en" })
        {
            var start = Channel("channel-authoring-ambiguous-start-" + culture);var answer = Channel("channel-authoring-ambiguous-answer-" + culture);
            var conversation = Guid.NewGuid();
            var (first, firstTechnical) = ChannelTurn(start, conversation, 1);
            Require(ChannelAccepted(start, first, firstTechnical));
            var previous = new ChannelObservation(first.RequestId, first, firstTechnical, 1, [], new(null, null));
            var (result, technical) = ChannelTurn(answer, conversation, 2);
            Require(ChannelAccepted(answer, result, technical, previous));
            Require(!ChannelAccepted(answer, result, technical, null));
            Require(!ChannelAccepted(answer, result, technical with { ConversationId = Guid.NewGuid() }, previous));
            Require(!ChannelAccepted(answer, result, technical, previous with { Result = first with { Proposal = first.Proposal! with {
                Payload = new FormAuthoringDecline("noPermission", null, null, "") } } }));
            Require(!ChannelAccepted(answer, result, technical, previous with { Result = first with { Proposal = first.Proposal! with { Payload = Form("home-authoring-form-es") } } }));
        }
    }

    public static void Home_instruction_hash_follows_the_sealed_version()
    {
        var manager = new ResourceManager("ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources.Assistant", typeof(IAiAssistantService).Assembly);
        foreach (var culture in new[] { "es", "en" })
        {
            var version2 = Program.HashPublicInstructions("home-system", 2, culture);
            Require(version2 == EvidenceWriter.HashPublicText(manager.GetString("Prompt.SystemV2", CultureInfo.GetCultureInfo(culture))!)
                && version2 != Program.HashPublicInstructions("home-system", 1, culture)
                && Program.HashPublicInstructions("home-system", 1, culture) == EvidenceWriter.HashPublicText(manager.GetString("Prompt.SystemV1", CultureInfo.GetCultureInfo(culture))!)
                && Program.HashPublicInstructions("capabilities-v1", 1, culture) == EvidenceWriter.HashPublicText(manager.GetString("Prompt.CapabilitiesV1", CultureInfo.GetCultureInfo(culture))!));
            foreach (var (id, version) in new[] { ("home-system", 3), ("capabilities-v1", 2), ("home-system", 0) })
            {
                var rejected = false;
                try { _ = Program.HashPublicInstructions(id, version, culture); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected);
            }
        }
    }

    public static void Ledger_continuation_keeps_its_previous_request_identity() => LedgerAsync().GetAwaiter().GetResult();
    private static async Task LedgerAsync()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "offline-ledger-" + Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var ledger = new SafeRunLedger(new("offline_run", AppContext.BaseDirectory, directory, "unused", "unused", "unused"));
            var entry = new QualificationLedgerEntry("offline-continuation", Guid.NewGuid(), Guid.NewGuid(), null, null, "before-submit")
                { OperationKind = "channel", ChannelId = "whatsapp-assistant", Culture = "es", SubjectUserId = Guid.NewGuid(), ChannelKind = "author",
                  InteractionMode = AssistantInteractionMode.Assistant, PreviousRequestId = Guid.NewGuid() };
            await ledger.SaveAsync(entry, CancellationToken.None);await ledger.SaveAsync(entry with { Phase = "accepted" }, CancellationToken.None);
            var rejected = false;
            try { await ledger.SaveAsync(entry with { PreviousRequestId = Guid.NewGuid() }, CancellationToken.None); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected);
            rejected = false;
            try { await ledger.SaveAsync(entry with { PreviousRequestId = null }, CancellationToken.None); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected);
            Require(!JsonSerializer.Serialize(entry with { PreviousRequestId = null }, JsonSerializerOptions.Web).Contains("previousRequestId", StringComparison.Ordinal));
            var resumed = QualificationChannelContexts.Resume(entry);
            Require(resumed.ChannelId == "whatsapp-assistant" && resumed.InteractionMode == AssistantInteractionMode.Assistant
                && resumed.SubjectUserId == entry.SubjectUserId && resumed.ActorUserId == entry.ActorUserId
                && QualificationChannelContexts.AuthoringCapabilities.All(id => resumed.AllowedCapabilities.Any(capability => capability.Id == id)));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Require(bool valid) { if (!valid) throw new InvalidOperationException("Offline authoring check failed."); }
}
