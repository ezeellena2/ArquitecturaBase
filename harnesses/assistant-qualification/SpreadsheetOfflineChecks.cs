using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenMcdf;

namespace AssistantQualification;

// Stage 2 (spreadsheets): the inventory by Id and version, the fixtures and what the real owner port prepares from them (without a
// database), what never reaches the model, the oracles of every mapping rule, the runner checks and the daily quota. No provider,
// secret store or database.
internal static class SpreadsheetOfflineChecks
{
    public static readonly Action[] All = [Capability_inventory_compares_id_and_version, Sets_offer_what_the_app_offers,
        Spreadsheet_channel_context_is_the_automatic_conversation_with_a_spreadsheet, Home_offer_gate_names_what_home_offers_otherwise,
        Spreadsheet_fixtures_cover_the_stage_cases_in_both_cultures, Spreadsheet_fixtures_stay_fictitious_and_generic,
        Spreadsheet_files_keep_their_committed_hashes, Real_port_prepares_each_fixture_as_the_cases_expect,
        Real_port_rejects_what_never_reaches_the_model, Real_port_lets_only_catalog_managers_attach,
        Spreadsheet_expectations_point_at_columns_of_their_fixture, Spreadsheet_ideal_proposals_pass_every_payload_check,
        Spreadsheet_mapping_rejects_wrong_headers_or_code_and_option_columns, Spreadsheet_mapping_keeps_the_value_by_type_and_the_detail,
        Spreadsheet_mapping_rejects_a_missing_extra_moved_or_renamed_field, Spreadsheet_list_takes_code_and_option_from_one_column,
        Spreadsheet_import_keeps_its_target_catalog_and_field_keys, Spreadsheet_payload_of_another_owner_or_version_fails,
        Spreadsheet_proposal_must_map_the_turns_spreadsheet, Home_spreadsheet_checks_accept_the_observed_turns,
        Home_spreadsheet_checks_reject_another_offer_or_version, Home_spreadsheet_turn_brings_or_inherits_the_file,
        Home_spreadsheet_without_text_asks_without_proposing, Home_spreadsheet_adjustment_keeps_the_conversation_and_name,
        Spreadsheet_injection_adds_no_proposal_and_keeps_the_destination, Channel_spreadsheet_checks_accept_the_observed_operations,
        Channel_spreadsheet_checks_reject_another_offer_version_or_copy, Channel_spreadsheet_answer_continues_the_question_with_its_spreadsheet,
        Channel_spreadsheet_question_names_nothing, Channel_spreadsheet_ledger_resumes_its_context,
        Spreadsheet_sets_keep_each_person_within_the_daily_quota, Spreadsheet_dictation_fixtures_say_the_request_in_both_formats,
        Private_spreadsheet_copies_are_write_once_with_their_media_type, Spreadsheet_case_sets_list_their_own_fixtures];

    private static readonly string[] Cultures = ["es", "en"];
    private static readonly string[] SpreadsheetSets = ["home-spreadsheets", "channel-spreadsheets", "ia-spreadsheets"];
    // The seeded fixture the oracles see in a spreadsheet run: the base forms and catalogs plus the import catalog.
    private static readonly QualificationChannelFixture Fixture = new(
        QualificationFormSeed.FormNames.Select(text => new QualificationForm(Guid.NewGuid(), Guid.NewGuid(), text.Alias, new Dictionary<string, Guid>())
            { Names = Names(text.Spanish, text.English), Prefix = text.Prefix }).ToArray(), [], [])
    {
        Catalogs = [..QualificationFormSeed.CatalogNames.Append(QualificationAuthoringSeed.ImportCatalog)
            .Select(text => new QualificationCatalog(text.Alias, Guid.NewGuid(), Names(text.Spanish, text.English)))],
    };
    private static readonly HashSet<string> ProposalIds = new(StringComparer.Ordinal) { AuthoringOracle.ProposeForm, AuthoringOracle.ProposeCatalog,
        SpreadsheetOracle.ProposeImport, AuthoringOracle.Decline, AuthoringOracle.PeopleCatalog, "forms.missing-request.propose", "forms.propose-registration",
        "forms.clarify-registration", "forms.propose-suggestion", "whatsapp.no-reply", "whatsapp.decline-form" };
    // What the real port prepares from each committed fixture: kind, sheet, rows and columns with content and the first row.
    private static readonly (string File, StructuredSpreadsheetKind Kind, string Sheet, int Rows, int Columns, string[] FirstRow)[] Facts =
    [
        ("contratistas.xlsx", StructuredSpreadsheetKind.Xlsx, "Contratistas", 11, 4, ["CUIT", "Razón social", "Rubro", "Zona"]),
        ("insumos.csv", StructuredSpreadsheetKind.Csv, "", 8, 3, ["LIM-001", "Lavandina 5 L", "2350.50"]),
        ("tipos-de-tarea.xlsx", StructuredSpreadsheetKind.Xlsx, "Tareas", 9, 1, ["Tarea"]),
        ("proveedores.xlsx", StructuredSpreadsheetKind.Xlsx, "Proveedores nuevos", 9, 5, ["Código", "Proveedor", "Actividad", "Localidad", "Alta"]),
        ("clientes-con-notas.xlsx", StructuredSpreadsheetKind.Xlsx, "Clientes", 7, 4, ["Código", "Cliente", "Zona", "Observaciones"]),
    ];

    private static Dictionary<string, string> Names(string es, string en) => new(StringComparer.Ordinal) { ["es"] = es, ["en"] = en };
    private static T[] Cases<T>(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", file)));
        return document.RootElement.GetProperty("cases").Deserialize<T[]>(JsonSerializerOptions.Web)!;
    }
    private static HomeCase[] HomeCases() => Cases<HomeCase>(Program.HomeSpreadsheetFile);
    private static ChannelCase[] ChannelCases() => Cases<ChannelCase>(Program.ChannelSpreadsheetFile);
    private static HomeCase Home(string id) => HomeCases().Single(testCase => testCase.Id == id);
    private static ChannelCase Channel(string id) => ChannelCases().Single(testCase => testCase.Id == id);
    private static string Stem(string id, string prefix) => id[prefix.Length..^3];
    private static string Request(string question, DictationAudioFixture? audio) => audio?.ExpectedTranscript ?? question;
    private static SpreadsheetFixture[] Files() => HomeCases().Select(testCase => testCase.Spreadsheet).Concat(ChannelCases().Select(testCase => testCase.Spreadsheet))
        .OfType<SpreadsheetFixture>().Select(sheet => sheet with { Inherited = false }).DistinctBy(sheet => sheet.FileName).ToArray();

    // The real owner port without a database: BusinessForms' registrations, the account of the request and a permission table.
    private sealed class FixedPermissions(IReadOnlyDictionary<Guid, string[]> granted) : IPermissionService
    {
        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>(granted.GetValueOrDefault(userId) ?? []);
        public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
            Task.FromResult((granted.GetValueOrDefault(userId) ?? []).Contains(permission, StringComparer.Ordinal));
    }
    private static ServiceProvider Port(IReadOnlyDictionary<Guid, string[]>? granted = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddScoped<QualificationCurrentUser>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<QualificationCurrentUser>());
        services.AddSingleton<IPermissionService>(new FixedPermissions(granted ?? new Dictionary<Guid, string[]>()));
        services.AddBusinessFormsApplication().AddBusinessFormsInfrastructure();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
    private static StructuredAttachmentPreparation Prepare(byte[] bytes, string name)
    {
        using var services = Port();
        using var scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IStructuredAttachmentReader>().PrepareAsync(bytes, name, CancellationToken.None).GetAwaiter().GetResult();
    }
    private static readonly Lazy<IReadOnlyDictionary<string, StructuredSpreadsheet>> Prepared = new(() =>
        Files().ToDictionary(sheet => sheet.FileName, sheet => Prepare(QualificationSpreadsheets.ReadAsync(sheet, CancellationToken.None).GetAwaiter().GetResult(),
            sheet.FileName).Spreadsheet ?? throw new InvalidOperationException("Offline spreadsheet fixture is not readable."), StringComparer.Ordinal));
    private static StructuredSpreadsheet Sheet(string fileName) => Prepared.Value[fileName];

    // The shape a correct owner proposal has for the case, built from its expectation and the sheet the port prepared.
    private static AssistantCapabilityPayload Ideal(AuthoringExpectation expected, string culture, StructuredSpreadsheet sheet) => expected switch
    {
        { Capability: AuthoringOracle.ProposeCatalog, Version: SpreadsheetOracle.MappedCatalogVersion } => new FormAuthoringMappedCatalogProposal(culture,
            expected.Name!, culture == "es" ? "Catálogo ficticio." : "Fictitious catalog.", expected.ValueType!, Capitalized(expected.Code!), Capitalized(expected.Option!),
            (expected.Fields ?? []).Select(field => new FormAuthoringProposedField(field.Name, field.Type)).ToArray(), Summary(expected.Mapping!, sheet)),
        { Capability: SpreadsheetOracle.ProposeImport } => new FormAuthoringImportProposal(new(Fixture.Catalog(expected.ExistingCatalog!).Id,
            Fixture.Catalog(expected.ExistingCatalog!).Names[culture], expected.ValueType!), Summary(expected.Mapping!, sheet)),
        { Capability: AuthoringOracle.Decline } => new FormAuthoringDecline("unclear", null, null, ""),
        _ => throw new InvalidOperationException("Offline spreadsheet fixture has no ideal payload."),
    };
    private static string Capitalized(string value) => char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
    private static FormAuthoringSpreadsheetSummary Summary(SpreadsheetMappingExpectation mapping, StructuredSpreadsheet sheet)
    {
        var header = mapping.HasHeaders ? sheet.Rows[0] : null;
        string? Header(string letter) => header?.Cells.GetValueOrDefault(letter);
        var columns = new List<FormAuthoringMappedColumn> { new("code", null, null, mapping.Code, Header(mapping.Code)), new("option", null, null, mapping.Option, Header(mapping.Option)) };
        if (mapping.Value is { } value) columns.Add(new("value", null, null, value, Header(value)));
        if (mapping.Detail is { } detail) columns.Add(new("detail", null, null, detail, Header(detail)));
        columns.AddRange((mapping.Fields ?? []).Select(field => new FormAuthoringMappedColumn("field", field.Name, field.Key, field.Column, Header(field.Column))));
        return new(sheet.FileName, sheet.SheetName, sheet.RowCount - (mapping.HasHeaders ? 1 : 0), mapping.HasHeaders, columns, []);
    }
    private static Dictionary<string, AuthoringExpectation> Expectations() => HomeCases().Select(testCase => (testCase.Id, testCase.Authoring!, testCase.Culture))
        .Concat(ChannelCases().Select(testCase => (testCase.Id, testCase.Authoring!, testCase.Culture))).ToDictionary(item => item.Id, item => item.Item2, StringComparer.Ordinal);
    private static AssistantCapabilityPayload? Previous(AuthoringExpectation expected, string culture, StructuredSpreadsheet sheet) =>
        expected.Continues is null ? null : Ideal(Expectations()[expected.Continues], culture, sheet);
    private static bool Passes(AuthoringExpectation expected, string culture, AssistantCapabilityPayload? payload, StructuredSpreadsheet sheet,
        AssistantCapabilityPayload? previous = null)
    {
        var checks = SpreadsheetOracle.PayloadChecks(expected, culture, payload, Fixture, sheet, previous);
        return checks.Count > 0 && checks.All(check => check.Passed) && checks.Select(check => check.Id).Distinct(StringComparer.Ordinal).Count() == checks.Count;
    }
    private static (AuthoringExpectation Expected, string Culture, StructuredSpreadsheet Sheet, T Ideal) Case<T>(string id) where T : AssistantCapabilityPayload
    {
        var (expected, culture, file) = id.StartsWith("home-", StringComparison.Ordinal)
            ? (Home(id).Authoring!, Home(id).Culture, Home(id).Spreadsheet!.FileName) : (Channel(id).Authoring!, Channel(id).Culture, Channel(id).Spreadsheet!.FileName);
        return (expected, culture, Sheet(file), (T)Ideal(expected, culture, Sheet(file)));
    }
    private static FormAuthoringSpreadsheetSummary Columns(FormAuthoringSpreadsheetSummary summary, params FormAuthoringMappedColumn[] columns) => summary with { Columns = columns };
    private static AssistantCapabilityDefinition Definition(string id, int version, bool home = false, bool attachment = false) =>
        new(id, version, AssistantCapabilityKind.Proposal, "Fictitious capability.", "{\"version\":" + version.ToString(CultureInfo.InvariantCulture) + "}", "{}",
            new([], []), new(1, 1, 1, 1)) { OfferedInHome = home, RequiresAttachment = attachment };
    private static bool Throws(Action action)
    {
        try { action(); return false; } catch (InvalidOperationException) { return true; } catch (ArgumentException) { return true; }
    }

    public static void Capability_inventory_compares_id_and_version()
    {
        AssistantCapabilityDefinition[] registered = [Definition("forms.authoring.forms", 1), Definition(AuthoringOracle.ProposeCatalog, 1),
            Definition(AuthoringOracle.ProposeCatalog, 2), Definition(SpreadsheetOracle.ProposeImport, 1, attachment: true)];
        var stage1 = Program.Inventory(registered, [new(AuthoringOracle.ProposeCatalog, 1), new("forms.authoring.forms", 1)]);
        Require(stage1.Select(stamp => (stamp.Id, stamp.Version)).SequenceEqual([("forms.authoring.forms", 1), (AuthoringOracle.ProposeCatalog, 1)])
            && stage1[1].InputSchemaSha256 == EvidenceWriter.HashPublicText("{\"version\":1}"));
        var stage2 = Program.Inventory(registered, [new(SpreadsheetOracle.ProposeImport, 1), new(AuthoringOracle.ProposeCatalog, 2)]);
        Require(stage2.Select(stamp => (stamp.Id, stamp.Version)).SequenceEqual([(AuthoringOracle.ProposeCatalog, 2), (SpreadsheetOracle.ProposeImport, 1)])
            && stage2[0].InputSchemaSha256 == EvidenceWriter.HashPublicText("{\"version\":2}"));
        // Comparing only Ids found propose-catalog twice and stopped every set before the provider: by Id and version it never does.
        Require(registered.Count(definition => definition.Id == AuthoringOracle.ProposeCatalog) == 2);
        Require(Throws(() => Program.Inventory(registered, [new(AuthoringOracle.ProposeCatalog, 3)])));
        Require(Throws(() => Program.Inventory(registered, [new(AuthoringOracle.ProposeCatalog, 1), new(AuthoringOracle.ProposeCatalog, 1)])));
        Require(Throws(() => Program.Inventory([..registered, Definition(AuthoringOracle.ProposeCatalog, 2)], [new(AuthoringOracle.ProposeCatalog, 2)])));
        Require(Throws(() => Program.Inventory(registered, [new("forms.authoring.catalogs", 1)])));
    }

    // Every definition some set names, plus version 1 of the catalog, still registered next to version 2 as in the app.
    private static AssistantCapabilityDefinition[] Registry() =>
    [
        ..Program.HomeAuthoringCapabilities.Concat(QualificationChannelContexts.References).Append(new(AuthoringOracle.ProposeCatalog, 1))
            .Distinct().Select(reference => Definition(reference.Id, reference.Version)),
    ];
    // What Stage 1 offered before the app moved to version 2: the catalog in version 1 and no import, in Home and in the channel.
    private static readonly AssistantCapabilityReference[] HomeStage1 = [..Program.HomeReferences, ..HomeRunner.AuthoringOffered
        .Select(reference => reference.Id == AuthoringOracle.ProposeCatalog ? new AssistantCapabilityReference(reference.Id, 1) : reference)];
    private static readonly AssistantCapabilityReference[] AutomaticStage1 = [..QualificationChannelContexts.AutomaticReferences
        .Where(reference => reference.Id != SpreadsheetOracle.ProposeImport)
        .Select(reference => reference.Id == AuthoringOracle.ProposeCatalog ? new AssistantCapabilityReference(reference.Id, 1) : reference)];

    public static void Sets_offer_what_the_app_offers()
    {
        Require(Registry().Count(definition => definition.Id == AuthoringOracle.ProposeCatalog) == 2);
        // Home: its five reads, and to an author the catalog in version 2 and the import (6e27fa0b), never version 1 or decline.
        Require(Program.HomeReferences.Length == 5 && Program.HomeReferences.All(reference => reference.Version == 1)
            && Program.HomeReferences.Select(reference => reference.Id).SequenceEqual(QualificationSeed.HomeCapabilities));
        Require(Program.HomeAuthoringCapabilities.Length == 12 && Program.HomeAuthoringCapabilities.Contains(new(AuthoringOracle.ProposeCatalog, 2))
            && Program.HomeAuthoringCapabilities.Contains(new(SpreadsheetOracle.ProposeImport, 1)) && !Program.HomeAuthoringCapabilities.Contains(new(AuthoringOracle.ProposeCatalog, 1))
            && !Program.HomeAuthoringCapabilities.Any(reference => reference.Id == AuthoringOracle.Decline)
            && HomeRunner.SpreadsheetOffered.SequenceEqual([..HomeRunner.AuthoringOffered, new(SpreadsheetOracle.ProposeImport, 1)]));
        // Every automatic kind is WhatsAppAssistantAccessPolicy's automatic conversation; the operator's manual one and C4 keep version 1.
        var actor = new QualificationActor(Guid.NewGuid(), "offline", "es", "fictitious");
        foreach (var kind in new[] { "auto", "catalog", "no-forms", "author" })
            Require(QualificationChannelContexts.Create(actor, actor.UserId, kind).AllowedCapabilities.SequenceEqual(QualificationChannelContexts.AutomaticReferences));
        Require(QualificationChannelContexts.AutomaticReferences.Length == 15 && QualificationChannelContexts.AutomaticReferences.Distinct().Count() == 15
            && QualificationChannelContexts.AutomaticReferences.Contains(new(AuthoringOracle.ProposeCatalog, 2))
            && QualificationChannelContexts.AutomaticReferences.Contains(new(SpreadsheetOracle.ProposeImport, 1))
            && !QualificationChannelContexts.AutomaticReferences.Contains(new(AuthoringOracle.ProposeCatalog, 1)));
        Require(QualificationChannelContexts.Create(actor, Guid.NewGuid(), "manual").AllowedCapabilities is { Count: 7 } manual
            && manual.All(reference => reference.Version == 1 && !reference.Id.StartsWith("forms.authoring.", StringComparison.Ordinal))
            && QualificationChannelContexts.Create(actor, Guid.NewGuid(), "auto", Guid.NewGuid()).AllowedCapabilities is { Count: 5 } captured
            && captured.All(reference => reference.Version == 1 && !reference.Id.StartsWith("forms.authoring.", StringComparison.Ordinal)));
        // The channel stamps: every reference some context offers, the catalog only in version 2.
        Require(QualificationChannelContexts.References.Length == 17 && QualificationChannelContexts.References.Distinct().Count() == 17
            && QualificationChannelContexts.References.Select(reference => reference.Id).SequenceEqual(QualificationChannelContexts.Capabilities)
            && QualificationChannelContexts.References.Single(reference => reference.Id == AuthoringOracle.ProposeCatalog).Version == 2);
        // Their inventories start next to the two registered versions of the catalog and stamp version 2.
        Require(Program.Inventory(Registry(), Program.HomeReferences).Length == 5 && Program.Inventory(Registry(), Program.HomeAuthoringCapabilities).Length == 12
            && Program.Inventory(Registry(), QualificationChannelContexts.References).Length == 17
            && Program.Inventory(Registry(), QualificationChannelContexts.References).Single(stamp => stamp.Id == AuthoringOracle.ProposeCatalog).Version == 2
            && Program.Inventory(Registry(), Program.HomeAuthoringCapabilities).Single(stamp => stamp.Id == AuthoringOracle.ProposeCatalog).Version == 2);
    }

    public static void Spreadsheet_channel_context_is_the_automatic_conversation_with_a_spreadsheet()
    {
        var person = new QualificationActor(Guid.NewGuid(), "offline", "en", "fictitious");
        var context = QualificationChannelContexts.Create(person, person.UserId, "author");
        Require(context is { ChannelId: "whatsapp-assistant", InteractionMode: AssistantInteractionMode.Assistant, Culture: "en", SourceRecordId: null }
            && context.ActorUserId == person.UserId && context.SubjectUserId == person.UserId && context.RequiredPermissions.Actor.Count == 0
            && context.AllowedTerminals.SequenceEqual([new AssistantTerminalReference("assistant_proposal", 1)]));
        // The same conversation with or without a spreadsheet: the engine adds the import only to a turn that has one.
        Require(context.AllowedCapabilities.SequenceEqual(QualificationChannelContexts.AutomaticReferences) && ChannelRunner.SpreadsheetAuthoring.All(context.AllowedCapabilities.Contains)
            && ChannelRunner.AuthoringBuilding.All(context.AllowedCapabilities.Contains) && !ChannelRunner.AuthoringBuilding.Contains(new(SpreadsheetOracle.ProposeImport, 1))
            && context.AllowedCapabilities.All(reference => reference.Id is not ("forms.suggestion-candidates" or "forms.propose-suggestion")));
        Require(Program.Inventory(Registry(), QualificationChannelContexts.AutomaticReferences).Length == 15);
        Require(Throws(() => QualificationChannelContexts.Create(person, person.UserId, "sheet-author")));
    }

    public static void Home_offer_gate_names_what_home_offers_otherwise()
    {
        AssistantCapabilityDefinition[] expected = [..HomeRunner.SpreadsheetOffered.Select(reference => Definition(reference.Id, reference.Version, home: true,
            attachment: reference.Id == SpreadsheetOracle.ProposeImport)), Definition(AuthoringOracle.ProposeCatalog, 1), Definition(AuthoringOracle.Decline, 1)];
        // As the app registers it today (6e27fa0b): the Home sets start.
        Require(Program.HomeOfferGaps(expected).Count == 0);
        // Before it, Home offered version 1 and no import: the sets say so and stop before seeding, with fixed public Ids only.
        var before = expected.Select(definition => definition.Id == SpreadsheetOracle.ProposeImport || definition is { Id: AuthoringOracle.ProposeCatalog, Version: 2 }
            ? definition with { OfferedInHome = false } : definition is { Id: AuthoringOracle.ProposeCatalog, Version: 1 } ? definition with { OfferedInHome = true } : definition).ToArray();
        Require(Program.HomeOfferGaps(before).SequenceEqual(["offer forms.authoring.propose-catalog@2", "offer forms.authoring.propose-import@1",
            "stop offering forms.authoring.propose-catalog@1"]));
        Require(Program.HomeOfferGaps(expected.Where(definition => definition.Id != AuthoringOracle.PeopleCatalog).ToArray())
            .SequenceEqual(["offer forms.catalog-filters.propose@1"]));
        // Offering what no Home set expects (decline) is not a gap of these sets.
        Require(Program.HomeOfferGaps([..expected.Where(definition => definition.Id != AuthoringOracle.Decline), Definition(AuthoringOracle.Decline, 1, home: true)]).Count == 0);
    }

    public static void Spreadsheet_fixtures_cover_the_stage_cases_in_both_cultures()
    {
        var home = HomeCases(); var channel = ChannelCases();
        Require(home.Length == 18 && channel.Length == 12 && home.Select(testCase => testCase.Id).Concat(channel.Select(testCase => testCase.Id)).Distinct(StringComparer.Ordinal).Count() == 30);
        Require(home.All(testCase => Regex.IsMatch(testCase.Id, "^home-spreadsheets-[a-z0-9-]+-(es|en)$", RegexOptions.CultureInvariant) && testCase.Id.EndsWith("-" + testCase.Culture, StringComparison.Ordinal))
            && channel.All(testCase => Regex.IsMatch(testCase.Id, "^channel-spreadsheets-[a-z0-9-]+-(es|en)$", RegexOptions.CultureInvariant) && testCase.Id.EndsWith("-" + testCase.Culture, StringComparison.Ordinal)));
        string[] homeStems = ["xlsx-catalog", "csv-catalog", "list-catalog", "import", "adjust-start", "adjust-change", "injection", "no-text", "dictated-webm"];
        string[] channelStems = ["catalog", "import", "injection", "question", "answer", "dictated-ogg"];
        foreach (var culture in Cultures)
        {
            Require(home.Where(testCase => testCase.Culture == culture).Select(testCase => Stem(testCase.Id, "home-spreadsheets-")).Order(StringComparer.Ordinal).SequenceEqual(homeStems.Order(StringComparer.Ordinal)));
            Require(channel.Where(testCase => testCase.Culture == culture).Select(testCase => Stem(testCase.Id, "channel-spreadsheets-")).Order(StringComparer.Ordinal).SequenceEqual(channelStems.Order(StringComparer.Ordinal)));
        }
        // Every case brings or inherits a spreadsheet and expects a proposal in its version, or none (Home asks) or the channel's question.
        Require(home.All(testCase => testCase is { ActorKind: "author", Spreadsheet: not null, Authoring: not null } && (testCase.Authoring.Capability is null
            ? testCase is { ExpectedKind: "clarification", RequiredTools.Length: 0, Question.Length: 0, Spreadsheet.Inherited: false } && Stem(testCase.Id, "home-spreadsheets-") == "no-text"
            : testCase.ExpectedKind == "proposal" && testCase.RequiredTools.Contains(testCase.Authoring.Capability, StringComparer.Ordinal)
                && testCase.Authoring is { Capability: AuthoringOracle.ProposeCatalog, Version: 2 } or { Capability: SpreadsheetOracle.ProposeImport, Version: 1 }
                && testCase.Authoring.Mapping is not null)));
        Require(channel.All(testCase => testCase is { ActorKind: "author", Spreadsheet: not null, Authoring.Capability: { } capability }
            && testCase.RequiredTools.Contains(capability, StringComparer.Ordinal) && testCase.ExpectedPayload == testCase.Authoring switch
            {
                { Capability: AuthoringOracle.ProposeCatalog, Version: 2, Mapping: not null } => "authoring-mapped-catalog",
                { Capability: SpreadsheetOracle.ProposeImport, Version: 1, Mapping: not null } => "authoring-import",
                { Capability: AuthoringOracle.Decline, DeclineReason: "unclear", Mapping: null } => "authoring-decline",
                _ => "unknown",
            }));
        Require(home.Concat<object>(channel).Count() == 30 && home.Where(testCase => testCase.Authoring!.Capability == SpreadsheetOracle.ProposeImport)
            .Concat<object>(channel.Where(testCase => testCase.Authoring!.Capability == SpreadsheetOracle.ProposeImport)).Count() == 4
            && home.Select(testCase => testCase.Authoring!).Concat(channel.Select(testCase => testCase.Authoring!))
                .Where(expected => expected.Capability == SpreadsheetOracle.ProposeImport).All(expected => expected is { ExistingCatalog: "suppliers", ValueType: "text" }));
        // Only a follow-up inherits: in Home the adjustment of its own culture and actor, after the start in the same conversation; by
        // WhatsApp the answer, right after the question it continues. Both keep the start's file.
        foreach (var change in home.Where(testCase => testCase.Authoring!.Continues is not null))
        {
            var start = home.Single(testCase => testCase.Id == change.Authoring!.Continues);
            Require(Array.IndexOf(home, start) < Array.IndexOf(home, change) && start.ConversationKey is { } key && change.ConversationKey == key
                && start.Culture == change.Culture && start.ActorKind == change.ActorKind && start.Authoring!.Name == change.Authoring!.Name
                && change.Spreadsheet is { Inherited: true } && start.Spreadsheet is { Inherited: false } && change.Spreadsheet.FileName == start.Spreadsheet.FileName);
        }
        foreach (var answer in channel.Where(testCase => testCase.Authoring!.Continues is not null))
        {
            var question = channel.Single(testCase => testCase.Id == answer.Authoring!.Continues);
            Require(Array.IndexOf(channel, question) == Array.IndexOf(channel, answer) - 1 && question.Culture == answer.Culture
                && question.Authoring is { DeclineReason: "unclear" } && answer.Spreadsheet is { Inherited: true } && question.Spreadsheet is { Inherited: false }
                && answer.Spreadsheet.FileName == question.Spreadsheet.FileName);
        }
        Require(home.Count(testCase => testCase.Spreadsheet!.Inherited) == 2 && channel.Count(testCase => testCase.Spreadsheet!.Inherited) == 2
            && home.Count(testCase => testCase.Spreadsheet!.FileName == QualificationSpreadsheets.InjectionFile) == 2
            && channel.Count(testCase => testCase.Spreadsheet!.FileName == QualificationSpreadsheets.InjectionFile) == 2);
        // A typed case says something, a dictated one only through its recording, and a spreadsheet without text says nothing at all.
        Require(home.All(testCase => testCase.Audio is null ? testCase.Question.Length <= 4000 && (testCase.Question.Length > 0) == (testCase.Authoring!.Capability is not null)
                : testCase.Question.Length == 0 && testCase.Audio.ContentType == "audio/webm")
            && channel.All(testCase => testCase.Audio is null ? testCase.Question.Length is > 0 and <= 4000 : testCase.Question.Length == 0 && testCase.Audio.ContentType == "audio/ogg"));
    }

    public static void Spreadsheet_fixtures_stay_fictitious_and_generic()
    {
        // Only generic fictitious business facts: no real company, link, email or phone-like number in what is said or expected.
        var texts = HomeCases().Select(testCase => Request(testCase.Question, testCase.Audio) + "\n" + JsonSerializer.Serialize(testCase.Authoring, JsonSerializerOptions.Web))
            .Concat(ChannelCases().Select(testCase => Request(testCase.Question, testCase.Audio) + "\n" + JsonSerializer.Serialize(testCase.Authoring, JsonSerializerOptions.Web))).ToArray();
        Require(texts.Length == 30 && texts.All(text => !Regex.IsMatch(text, @"macrosistemas|\bocha\b|https?:|www\.|[\w.+-]+@[\w-]+\.\w", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            && !Regex.IsMatch(text, @"\d[\d\s().-]{5,}\d", RegexOptions.CultureInvariant)));
        // Every cell of every committed spreadsheet, not only the prepared sample: invented names and CUITs in an obviously synthetic range.
        foreach (var sheet in Files())
        {
            var cells = Cells(sheet);
            Require(cells.Count > 0 && cells.All(cell => !Regex.IsMatch(cell, @"macrosistemas|\bocha\b|https?:|www\.|@|\+\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                && !Regex.IsMatch(cell, @"\d[\d\s().]{7,}\d", RegexOptions.CultureInvariant)));
            Require(cells.Where(cell => Regex.IsMatch(cell, @"^\d{2}-\d{8}-\d$", RegexOptions.CultureInvariant))
                .All(cuit => Regex.IsMatch(cuit, @"^30-900000\d{2}-\d$", RegexOptions.CultureInvariant)));
        }
        Require(Cells(Files().Single(sheet => sheet.FileName == "contratistas.xlsx")).Count(cell => Regex.IsMatch(cell, @"^30-900000\d{2}-\d$", RegexOptions.CultureInvariant)) == 10);
    }
    private static List<string> Cells(SpreadsheetFixture sheet)
    {
        var bytes = QualificationSpreadsheets.ReadAsync(sheet, CancellationToken.None).GetAwaiter().GetResult();
        if (sheet.FileName.EndsWith(".csv", StringComparison.Ordinal))
            return [..new UTF8Encoding(false, true).GetString(bytes).Split('\n', ';').Where(cell => cell.Length > 0)];
        using var content = new MemoryStream(bytes, false);
        using var workbook = new XLWorkbook(content);
        return [..workbook.Worksheets.SelectMany(worksheet => worksheet.CellsUsed()).Select(cell => cell.GetFormattedString())];
    }

    public static void Spreadsheet_files_keep_their_committed_hashes()
    {
        var descriptors = HomeCases().Select(testCase => testCase.Spreadsheet!).Concat(ChannelCases().Select(testCase => testCase.Spreadsheet!)).ToArray();
        Require(descriptors.Length == 30 && descriptors.All(QualificationSpreadsheets.IsValid));
        // Every case that names a file agrees on its size and hash, and the bytes are those.
        foreach (var file in descriptors.GroupBy(sheet => sheet.FileName))
        {
            Require(file.Select(sheet => (sheet.ByteCount, sheet.Sha256)).Distinct().Count() == 1);
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "spreadsheets", file.Key));
            Require(bytes.LongLength == file.First().ByteCount && EvidenceWriter.HashPublicBytes(bytes) == file.First().Sha256);
        }
        // The folder holds exactly the five fixtures the cases use (and the attributes that keep a CSV's bytes).
        var committed = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "spreadsheets")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Require(committed.SequenceEqual(Files().Select(sheet => sheet.FileName).Append(".gitattributes").Order(StringComparer.Ordinal)) && Files().Length == 5);
        var changed = descriptors[0] with { Sha256 = new string('a', 64) };
        Require(Throws(() => QualificationSpreadsheets.ReadAsync(changed, CancellationToken.None).GetAwaiter().GetResult()));
        Require(!QualificationSpreadsheets.IsValid(descriptors[0] with { FileName = "../contratistas.xlsx" })
            && !QualificationSpreadsheets.IsValid(descriptors[0] with { FileName = "precios.xls" })
            && !QualificationSpreadsheets.IsValid(descriptors[0] with { ByteCount = AssistantAttachmentInput.MaximumBytes + 1 })
            && !QualificationSpreadsheets.IsValid(descriptors[0] with { Sha256 = "NOT-A-HASH" }));
    }

    public static void Real_port_prepares_each_fixture_as_the_cases_expect()
    {
        Require(Facts.Length == Files().Length);
        foreach (var (file, kind, sheetName, rows, columns, firstRow) in Facts)
        {
            var sheet = Sheet(file);var descriptor = Files().Single(item => item.FileName == file);
            Require(sheet.FileName == file && sheet.Kind == kind && sheet.SheetName == sheetName && sheet.RowCount == rows && sheet.ColumnCount == columns
                && sheet.MediaType == (kind == StructuredSpreadsheetKind.Csv ? QualificationSpreadsheets.CsvMediaType : QualificationSpreadsheets.XlsxMediaType)
                && sheet.Size == descriptor.ByteCount && sheet.Sha256 == descriptor.Sha256);
            // The first rows with content, as the model reads them: the first one in its letters, none cut and none past the last column.
            Require(sheet.Rows.Count == 6 && sheet.Rows[0].Number == 1 && sheet.Rows[0].Cells.Keys.SequenceEqual(firstRow.Select((_, index) => ((char)('A' + index)).ToString()))
                && sheet.Rows[0].Cells.Values.SequenceEqual(firstRow) && sheet.Rows.All(row => row.Cells.Values.All(value => !value.EndsWith('…'))
                    && row.Cells.Keys.All(letter => letter.Length == 1 && letter[0] - 'A' < columns)));
            Require(JsonSerializer.SerializeToUtf8Bytes(sheet, JsonSerializerOptions.Web).Length <= StructuredSpreadsheet.MaximumBytes);
        }
        Require(Sheet("proveedores.xlsx").Rows[1].Cells["E"] == "2026-03-02" && Sheet("insumos.csv").Rows[1].Cells["C"] == "1180.00");
        // Both untrusted notes reach the model whole, each in its own cell, and only the injection fixture carries them.
        Require(QualificationSpreadsheets.InjectedNotes.All(note => note.Length <= 80) && QualificationSpreadsheets.CarriesInjectedNotes(Sheet(QualificationSpreadsheets.InjectionFile))
            && Sheet(QualificationSpreadsheets.InjectionFile).Rows[2].Cells["D"] == QualificationSpreadsheets.InjectedNotes[0]
            && Sheet(QualificationSpreadsheets.InjectionFile).Rows[3].Cells["D"] == QualificationSpreadsheets.InjectedNotes[1]
            && Files().Where(sheet => sheet.FileName != QualificationSpreadsheets.InjectionFile).All(sheet => !QualificationSpreadsheets.CarriesInjectedNotes(Sheet(sheet.FileName))));
    }

    public static void Real_port_rejects_what_never_reaches_the_model()
    {
        // Each reason the owner gives through the real port, from bytes built here: nothing is prepared, so nothing reaches the model.
        foreach (var (bytes, name, reason) in new[]
        {
            (Compound("Workbook"), "precios.xls", StructuredAttachmentRejection.Legacy),
            (Compound("Workbook"), "precios.xlsx", StructuredAttachmentRejection.Legacy),
            (Compound("EncryptionInfo", "EncryptedPackage"), "precios.xlsx", StructuredAttachmentRejection.Protected),
            (MacroEnabled(), "precios.xlsm", StructuredAttachmentRejection.MacroEnabled),
            (MacroEnabled(), "precios.xlsx", StructuredAttachmentRejection.MacroEnabled),
            (Enumerable.Repeat((byte)'a', (int)AssistantAttachmentInput.MaximumBytes + 1).ToArray(), "grande.csv", StructuredAttachmentRejection.TooLarge),
            (Encoding.UTF8.GetBytes(string.Join(';', Enumerable.Range(1, 65).Select(index => "C" + index.ToString(CultureInfo.InvariantCulture))) + "\n"), "ancha.csv", StructuredAttachmentRejection.TooLarge),
            (Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, BusinessFormCatalog.MaxItems + 3).Select(index => "A" + index.ToString(CultureInfo.InvariantCulture) + "\n"))), "larga.csv", StructuredAttachmentRejection.TooLarge),
            (Array.Empty<byte>(), "vacia.csv", StructuredAttachmentRejection.Empty),
            (Encoding.UTF8.GetBytes("\n,\n \n"), "vacia.csv", StructuredAttachmentRejection.Empty),
            (Workbook(_ => { }), "vacia.xlsx", StructuredAttachmentRejection.Empty),
            (Encoding.Latin1.GetBytes("Código;Descripción\n1;Café\n"), "insumos.csv", StructuredAttachmentRejection.Unreadable),
            (Compound("WordDocument"), "precios.xls", StructuredAttachmentRejection.Unreadable),
        })
        {
            var prepared = Prepare(bytes, name);
            Require(prepared.Spreadsheet is null && prepared.Rejection == reason);
        }
        // The same text saved as UTF-8 reads: only the encoding was the reason.
        Require(Prepare(Encoding.UTF8.GetBytes("Código;Descripción\n1;Café\n"), "insumos.csv").Spreadsheet is { RowCount: 2, ColumnCount: 2 });
    }
    // An OLE2 (CFB) container as Excel writes them: an old .xls keeps its sheets in «Workbook»; one with a password, «EncryptedPackage».
    private static byte[] Compound(params string[] streams)
    {
        using var content = new MemoryStream();
        using (var root = RootStorage.Create(content, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
            foreach (var name in streams)
            {
                using var stream = root.CreateStream(name);
                stream.Write(Encoding.ASCII.GetBytes("fictitious " + name));
            }
        return content.ToArray();
    }
    private static byte[] Workbook(Action<IXLWorksheet> configure)
    {
        using var workbook = new XLWorkbook();
        configure(workbook.AddWorksheet("Hoja"));
        using var content = new MemoryStream();
        workbook.SaveAs(content);
        return content.ToArray();
    }
    // The same workbook as Excel saves it with macros: the type of its main part says so in the manifest.
    private static byte[] MacroEnabled()
    {
        using var content = new MemoryStream();
        content.Write(Workbook(sheet => { sheet.Cell(1, 1).Value = "Código"; sheet.Cell(2, 1).Value = "A1"; }));
        content.Position = 0;
        using (var document = SpreadsheetDocument.Open(content, true)) document.ChangeDocumentType(SpreadsheetDocumentType.MacroEnabledWorkbook);
        return content.ToArray();
    }

    public static void Real_port_lets_only_catalog_managers_attach()
    {
        Guid author = Guid.NewGuid(), channelAuthor = Guid.NewGuid(), reader = Guid.NewGuid(), recipient = Guid.NewGuid();
        var granted = new Dictionary<Guid, string[]>
        {
            [author] = QualificationAuthoringSeed.Building,
            [channelAuthor] = [..QualificationAuthoringSeed.Building, BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead],
            [reader] = [BusinessFormsPermissions.CatalogsRead],
            [recipient] = [BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead],
        };
        using var services = Port(granted);
        using var scope = services.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<IStructuredAttachmentReader>();
        bool Can(Guid account) => port.CanAttachAsync(account, CancellationToken.None).GetAwaiter().GetResult();
        // The seeded authors of both sets may attach; reading catalogs alone, capturing records or an unknown account may not.
        Require(Can(author) && Can(channelAuthor) && !Can(reader) && !Can(recipient) && !Can(Guid.NewGuid()));
    }

    public static void Spreadsheet_expectations_point_at_columns_of_their_fixture()
    {
        foreach (var (question, audio, sheetFixture, expected) in HomeCases().Select(testCase => (testCase.Question, testCase.Audio, testCase.Spreadsheet!, testCase.Authoring!))
            .Concat(ChannelCases().Select(testCase => (testCase.Question, testCase.Audio, testCase.Spreadsheet!, testCase.Authoring!))))
        {
            if (expected.Mapping is not { } mapping) continue;
            var sheet = Sheet(sheetFixture.FileName);var request = Request(question, audio);
            string[] letters = [mapping.Code, mapping.Option, ..new[] { mapping.Value, mapping.Detail }.OfType<string>(), ..(mapping.Fields ?? []).Select(field => field.Column)];
            Require(letters.All(letter => Regex.IsMatch(letter, "^[A-Z]$", RegexOptions.CultureInvariant) && letter[0] - 'A' < sheet.ColumnCount));
            // A list has one column; the others never repeat a column but the code and the option of a list.
            Require((mapping.Code == mapping.Option) == (sheet.ColumnCount == 1) && letters.Skip(2).Distinct(StringComparer.Ordinal).Count() == letters.Length - 2
                && letters.Skip(2).All(letter => letter != mapping.Code && letter != mapping.Option));
            // With headers a field and a detail are named by their header in the request; without them, every column by its letter.
            if (mapping.HasHeaders)
                Require(new[] { mapping.Detail }.OfType<string>().Concat((mapping.Fields ?? []).Select(field => field.Column))
                    .All(letter => AuthoringOracle.ContainsPhrase(request, sheet.Rows[0].Cells[letter])));
            else Require(letters.All(letter => Regex.IsMatch(request, @"(?<![\p{L}\p{N}])" + letter + @"(?![\p{L}\p{N}])", RegexOptions.CultureInvariant)));
            // An import names the catalog's fields as the catalogs read names them in the conversation language, with their keys.
            if (expected.Capability == SpreadsheetOracle.ProposeImport)
                Require((mapping.Fields ?? []).All(field => QualificationAuthoringSeed.ImportFields.Count(known => known.Key == field.Key
                    && (field.Name == known.Spanish || field.Name == known.English)) == 1 && AuthoringOracle.ContainsPhrase(request, field.Name))
                    && QualificationAuthoringSeed.ImportFields.Where(known => known.Required).All(known => (mapping.Fields ?? []).Any(field => field.Key == known.Key)));
        }
    }

    public static void Spreadsheet_ideal_proposals_pass_every_payload_check()
    {
        foreach (var (expected, culture, file) in HomeCases().Where(testCase => testCase.Authoring!.Capability is not null).Select(testCase => (testCase.Authoring!, testCase.Culture, testCase.Spreadsheet!.FileName))
            .Concat(ChannelCases().Select(testCase => (testCase.Authoring!, testCase.Culture, testCase.Spreadsheet!.FileName))))
            Require(Passes(expected, culture, Ideal(expected, culture, Sheet(file)), Sheet(file), Previous(expected, culture, Sheet(file))));
        Require(SpreadsheetOracle.PayloadChecks(Home("home-spreadsheets-no-text-es").Authoring!, "es", null, Fixture, Sheet("contratistas.xlsx"), null).Count == 0);
    }

    public static void Spreadsheet_mapping_rejects_wrong_headers_or_code_and_option_columns()
    {
        var (expected, culture, sheet, ideal) = Case<FormAuthoringMappedCatalogProposal>("home-spreadsheets-xlsx-catalog-es");
        var summary = ideal.Spreadsheet!;var columns = summary.Columns;
        Require(Passes(expected, culture, ideal, sheet));
        Require(!Passes(expected, culture, ideal with { Spreadsheet = summary with { HasHeaders = false } }, sheet));
        Require(!Passes(expected, culture, ideal with { Spreadsheet = summary with { RowCount = summary.RowCount + 1 } }, sheet));
        Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(summary, columns[0] with { Letter = "C" }, columns[1], columns[2], columns[3]) }, sheet));
        Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(summary, columns[0], columns[1] with { Letter = "A" }, columns[2], columns[3]) }, sheet));
        Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(summary, columns[0] with { Letter = "B" }, columns[1] with { Letter = "A" }, columns[2], columns[3]) }, sheet));
        Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(summary, columns[1], columns[0], columns[2], columns[3]) }, sheet));
        // Code and option describe the requested concepts and not each other's.
        Require(!Passes(expected, culture, ideal with { Code = ideal.Option }, sheet) && !Passes(expected, culture, ideal with { Option = "Nombre" }, sheet));
        var (csv, csvCulture, csvSheet, csvIdeal) = Case<FormAuthoringMappedCatalogProposal>("channel-spreadsheets-answer-es");
        Require(Passes(csv, csvCulture, csvIdeal, csvSheet, Previous(csv, csvCulture, csvSheet)));
        Require(!Passes(csv, csvCulture, csvIdeal with { Spreadsheet = csvIdeal.Spreadsheet! with { HasHeaders = true, RowCount = 7 } }, csvSheet, Previous(csv, csvCulture, csvSheet)));
    }

    public static void Spreadsheet_mapping_keeps_the_value_by_type_and_the_detail()
    {
        var (csv, culture, csvSheet, ideal) = Case<FormAuthoringMappedCatalogProposal>("home-spreadsheets-csv-catalog-es");
        var columns = ideal.Spreadsheet!.Columns;
        Require(Passes(csv, culture, ideal, csvSheet) && columns[2] is { Target: "value", Letter: "C" });
        // A decimal value takes its column; without it, in another one or with the value type of text, the mapping is not the one asked for.
        Require(!Passes(csv, culture, ideal with { Spreadsheet = Columns(ideal.Spreadsheet, columns[0], columns[1]) }, csvSheet));
        Require(!Passes(csv, culture, ideal with { Spreadsheet = Columns(ideal.Spreadsheet, columns[0], columns[1], columns[2] with { Letter = "B" }) }, csvSheet));
        Require(!Passes(csv, culture, ideal with { ValueType = "integer" }, csvSheet) && !Passes(csv, culture, ideal with { ValueType = "text" }, csvSheet));
        Require(!Passes(csv, culture, ideal with { Spreadsheet = Columns(ideal.Spreadsheet, columns[0], columns[1], columns[2] with { Target = "detail" }) }, csvSheet));
        // A text catalog never maps a value.
        var (text, textCulture, textSheet, textIdeal) = Case<FormAuthoringMappedCatalogProposal>("home-spreadsheets-xlsx-catalog-en");
        var textColumns = textIdeal.Spreadsheet!.Columns;
        Require(!Passes(text, textCulture, textIdeal with { Spreadsheet = Columns(textIdeal.Spreadsheet, [textColumns[0], textColumns[1], new("value", null, null, "C", "Rubro"), ..textColumns.Skip(2)]) }, textSheet));
        // The detail of the adjustment is its column, never left out, moved or added where nobody asked for it.
        var (change, changeCulture, changeSheet, changeIdeal) = Case<FormAuthoringMappedCatalogProposal>("home-spreadsheets-adjust-change-es");
        var previous = Previous(change, changeCulture, changeSheet);var changeColumns = changeIdeal.Spreadsheet!.Columns;
        Require(Passes(change, changeCulture, changeIdeal, changeSheet, previous) && changeColumns[2] is { Target: "detail", Letter: "C" });
        Require(!Passes(change, changeCulture, changeIdeal with { Spreadsheet = Columns(changeIdeal.Spreadsheet, changeColumns[0], changeColumns[1], changeColumns[3]) }, changeSheet, previous));
        Require(!Passes(change, changeCulture, changeIdeal with { Spreadsheet = Columns(changeIdeal.Spreadsheet, changeColumns[0], changeColumns[1], changeColumns[2] with { Letter = "D" }, changeColumns[3] with { Letter = "C" }) }, changeSheet, previous));
        Require(!Passes(text, textCulture, textIdeal with { Spreadsheet = Columns(textIdeal.Spreadsheet, [textColumns[0], textColumns[1], new("detail", null, null, "C", "Rubro"), ..textColumns.Skip(2)]) }, textSheet));
    }

    public static void Spreadsheet_mapping_rejects_a_missing_extra_moved_or_renamed_field()
    {
        var (expected, culture, sheet, ideal) = Case<FormAuthoringMappedCatalogProposal>("home-spreadsheets-xlsx-catalog-es");
        var summary = ideal.Spreadsheet!;var columns = summary.Columns;
        Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(summary, columns[0], columns[1], columns[2]) }, sheet));
        Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(summary, columns[0], columns[1], columns[2] with { Letter = "D" }, columns[3] with { Letter = "C" }) }, sheet));
        Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(summary, columns[0], columns[1], columns[3], columns[2]) }, sheet));
        Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(summary, columns[0], columns[1], columns[2] with { Field = "Región" }, columns[3]) }, sheet));
        Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(summary, columns[0], columns[1], columns[2] with { Key = "rubro" }, columns[3]) }, sheet));
        // The catalog's own fields keep their names, types and order.
        Require(!Passes(expected, culture, ideal with { Fields = [..ideal.Fields.Reverse()] }, sheet) && !Passes(expected, culture, ideal with { Fields = [ideal.Fields[0]] }, sheet)
            && !Passes(expected, culture, ideal with { Fields = [ideal.Fields[0] with { Type = "integer" }, ideal.Fields[1]] }, sheet));
        // The injection fixture's notes column is never mapped as one more field.
        var (injection, injectionCulture, injectionSheet, injectionIdeal) = Case<FormAuthoringMappedCatalogProposal>("channel-spreadsheets-injection-en");
        Require(Passes(injection, injectionCulture, injectionIdeal, injectionSheet));
        Require(!Passes(injection, injectionCulture, injectionIdeal with { Fields = [..injectionIdeal.Fields, new("Notes", "text")],
            Spreadsheet = Columns(injectionIdeal.Spreadsheet!, [..injectionIdeal.Spreadsheet!.Columns, new("field", "Notes", null, "D", "Observaciones")]) }, injectionSheet));
    }

    public static void Spreadsheet_list_takes_code_and_option_from_one_column()
    {
        foreach (var id in new[] { "home-spreadsheets-list-catalog-es", "home-spreadsheets-dictated-webm-en", "channel-spreadsheets-dictated-ogg-es" })
        {
            var (expected, culture, sheet, ideal) = Case<FormAuthoringMappedCatalogProposal>(id);
            var columns = ideal.Spreadsheet!.Columns;
            Require(Passes(expected, culture, ideal, sheet) && columns.Count == 2 && columns[0].Letter == "A" && columns[1].Letter == "A");
            Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(ideal.Spreadsheet, columns[0], columns[1] with { Letter = "B" }) }, sheet));
            Require(!Passes(expected, culture, ideal with { Spreadsheet = ideal.Spreadsheet with { HasHeaders = false, RowCount = sheet.RowCount } }, sheet));
            // A list describes both alike; anything else still has to name the task.
            Require(!Passes(expected, culture, ideal with { Option = culture == "es" ? "Nombre" : "Name" }, sheet));
        }
        // A sheet with more columns does not take code and option from one of them unless asked.
        var (expected2, culture2, sheet2, ideal2) = Case<FormAuthoringMappedCatalogProposal>("home-spreadsheets-xlsx-catalog-en");
        Require(!Passes(expected2, culture2, ideal2 with { Spreadsheet = Columns(ideal2.Spreadsheet!, [ideal2.Spreadsheet!.Columns[0], ideal2.Spreadsheet.Columns[1] with { Letter = "A" }, ..ideal2.Spreadsheet.Columns.Skip(2)]) }, sheet2));
    }

    public static void Spreadsheet_import_keeps_its_target_catalog_and_field_keys()
    {
        foreach (var id in new[] { "home-spreadsheets-import-es", "home-spreadsheets-import-en", "channel-spreadsheets-import-es", "channel-spreadsheets-import-en" })
        {
            var (expected, culture, sheet, ideal) = Case<FormAuthoringImportProposal>(id);
            var columns = ideal.Spreadsheet.Columns;
            Require(Passes(expected, culture, ideal, sheet) && columns.Where(column => column.Target == "field").Select(column => column.Key).SequenceEqual(["rubro", "ciudad"]));
            // Never another catalog, a cell may say so or not.
            Require(!Passes(expected, culture, ideal with { Catalog = ideal.Catalog with { Id = Fixture.Catalog("colors").Id, Name = "Color" } }, sheet));
            Require(!Passes(expected, culture, ideal with { Catalog = ideal.Catalog with { Id = Guid.NewGuid() } }, sheet));
            Require(!Passes(expected, culture, ideal with { Catalog = ideal.Catalog with { ValueType = "integer" } }, sheet));
            // Its fields by their keys and names, without the required one left out or the column nobody imports.
            Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(ideal.Spreadsheet, columns[0], columns[1], columns[2] with { Key = null }, columns[3]) }, sheet));
            Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(ideal.Spreadsheet, columns[0], columns[1], columns[2] with { Key = "ciudad" }, columns[3] with { Key = "rubro" }) }, sheet));
            Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(ideal.Spreadsheet, columns[0], columns[1], columns[3]) }, sheet));
            Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(ideal.Spreadsheet, columns[0], columns[1], columns[2], columns[3] with { Letter = "E" }) }, sheet));
            Require(!Passes(expected, culture, ideal with { Spreadsheet = Columns(ideal.Spreadsheet, [..columns, new("field", "Alta", "alta", "E", "Alta")]) }, sheet));
        }
    }

    public static void Spreadsheet_payload_of_another_owner_or_version_fails()
    {
        foreach (var (expected, culture, file) in HomeCases().Where(testCase => testCase.Authoring!.Capability is not null).Select(testCase => (testCase.Authoring!, testCase.Culture, testCase.Spreadsheet!.FileName))
            .Concat(ChannelCases().Select(testCase => (testCase.Authoring!, testCase.Culture, testCase.Spreadsheet!.FileName))))
        {
            var sheet = Sheet(file);var previous = Previous(expected, culture, sheet);
            var ideal = SpreadsheetOracle.PayloadChecks(expected, culture, Ideal(expected, culture, sheet), Fixture, sheet, previous);
            var mapped = (FormAuthoringMappedCatalogProposal)Ideal(Home("home-spreadsheets-xlsx-catalog-es").Authoring!, "es", Sheet("contratistas.xlsx"));
            var imported = (FormAuthoringImportProposal)Ideal(Home("home-spreadsheets-import-es").Authoring!, "es", Sheet("proveedores.xlsx"));
            // Version 1's catalog, the other spreadsheet proposal, a rejection or nothing: never the expected payload.
            foreach (AssistantCapabilityPayload? other in new AssistantCapabilityPayload?[] { null, mapped.ToCatalog(), new FormAuthoringRejection([new("mapping.code", "required")]),
                expected.Capability == SpreadsheetOracle.ProposeImport ? mapped : imported, new FormAuthoringDecline("noPermission", null, null, "") })
            {
                if (expected.Capability == AuthoringOracle.Decline && other is FormAuthoringDecline) continue;
                var checks = SpreadsheetOracle.PayloadChecks(expected, culture, other, Fixture, sheet, previous);
                Require(checks.Select(check => check.Id).SequenceEqual(ideal.Select(check => check.Id)) && checks.Single(check => check.Id == "authoring-payload-type") is { Passed: false });
            }
        }
        Require(Throws(() => SpreadsheetOracle.PayloadChecks(Home("home-spreadsheets-xlsx-catalog-es").Authoring! with { Version = 1 }, "es", null, Fixture, Sheet("contratistas.xlsx"), null)));
        Require(Throws(() => SpreadsheetOracle.PayloadChecks(new AuthoringExpectation(AuthoringOracle.ProposeForm), "es", null, Fixture, Sheet("contratistas.xlsx"), null)));
    }

    public static void Spreadsheet_proposal_must_map_the_turns_spreadsheet()
    {
        var (expected, culture, sheet, ideal) = Case<FormAuthoringMappedCatalogProposal>("home-spreadsheets-xlsx-catalog-es");
        // A catalog without the mapping, or mapping another file or sheet, is not built from the turn's spreadsheet.
        Require(!Passes(expected, culture, ideal with { Spreadsheet = null }, sheet));
        Require(!Passes(expected, culture, ideal with { Spreadsheet = ideal.Spreadsheet! with { FileName = "proveedores.xlsx" } }, sheet));
        Require(!Passes(expected, culture, ideal with { Spreadsheet = ideal.Spreadsheet! with { SheetName = "Hoja1" } }, sheet));
        Require(!Passes(expected, culture, ideal, Sheet("proveedores.xlsx")));
        Require(!Passes(expected, culture, ideal with { Name = "Proveedores" }, sheet) && !Passes(expected, culture, ideal with { Culture = "en" }, sheet));
    }

    // A Home turn as the engine would answer it for the case: the ideal proposal or none, its bubble or the inherited copy, the offer.
    private static (AiAssistantTurnResponse Result, TurnMetadata Technical) HomeTurn(HomeCase testCase, Guid conversation, int ordinal, string? inheritedKey = null,
        IReadOnlyList<AssistantCapabilityReference>? offered = null, AssistantCapabilityPayload? payload = null)
    {
        var expected = testCase.Authoring!;var sheet = Sheet(testCase.Spreadsheet!.FileName);
        var turn = Guid.NewGuid();var requestId = Guid.NewGuid();
        payload ??= expected.Capability is null ? null : Ideal(expected, testCase.Culture, sheet);
        var reads = testCase.RequiredTools.Select((tool, index) => tool == "forms.authoring.catalogs"
            ? new AssistantObservedRead(turn, index + 1, new(tool, 1), new FormAuthoringCatalogsPayload(true, [])) : null).OfType<AssistantObservedRead>().ToArray();
        var observed = payload is null ? null : new AssistantObservedProposal(turn, Guid.NewGuid(), new(expected.Capability!, expected.Version), payload) { ObservedReads = reads };
        var result = new AiAssistantTurnResponse(turn, conversation, requestId, ordinal, ordinal, 1, "completed", "completed", "allowed", testCase.Question,
            new AiAssistantAnswer(payload is null ? testCase.ExpectedKind : "proposal", "Texto ficticio.", []), [], null, null, testCase.Culture,
            "America/Argentina/Buenos_Aires", DateTime.UnixEpoch, DateTime.UnixEpoch, 2)
        {
            ObservedProposal = observed,
            Proposal = observed is null ? null : new AiAssistantProposalResponse(observed.Id, observed.Capability.Id, observed.Capability.Version, JsonSerializer.SerializeToElement(new { })),
            Attachment = testCase.Spreadsheet.Inherited ? null
                : new AiAssistantTurnAttachmentResponse(testCase.Spreadsheet.FileName, QualificationSpreadsheets.Kind(sheet), sheet.RowCount, testCase.Spreadsheet.ByteCount),
        };
        var tools = testCase.RequiredTools.Select(tool => new ObservedCapability(tool, tool == expected.Capability ? expected.Version : 1, 128,
            ProposalIds.Contains(tool) ? "proposed" : "complete")).ToArray();
        var references = offered ?? Program.HomeAuthoringCapabilities;
        var technical = new TurnMetadata(3, tools.Length, 512, null, null, true, null, new("gpt-6.1-sol-candidate-1", 1, "public", "home-system", 2, "home"), tools)
        {
            ConversationId = conversation, Ordinal = ordinal, OfferedReferences = references, OfferedCapabilities = references.Select(reference => reference.Id).ToArray(),
            AttachmentKey = inheritedKey ?? QualificationSpreadsheets.HomeKey(requestId, sheet.Kind), AttachmentSha256 = testCase.Spreadsheet.Sha256,
            AttachmentSize = testCase.Spreadsheet.ByteCount,
        };
        return (result, technical);
    }
    private static bool HomeAccepted(HomeCase testCase, AiAssistantTurnResponse result, TurnMetadata technical, (Guid Id, long Revision)? conversation = null,
        AssistantObservedProposal? previous = null, string? previousKey = null, StructuredSpreadsheet? sheet = null)
    {
        var checks = new List<CheckEvidence>();
        HomeRunner.AddSpreadsheetChecks(checks, testCase, result, technical, Fixture, sheet ?? Sheet(testCase.Spreadsheet!.FileName), ProposalIds, conversation, previous, previousKey);
        return checks.Count > 0 && checks.All(check => check.Passed) && checks.Select(check => check.Id).Distinct(StringComparer.Ordinal).Count() == checks.Count;
    }
    private static (HomeCase Change, AiAssistantTurnResponse Result, TurnMetadata Technical, (Guid, long) Conversation, AssistantObservedProposal Previous, string Key) Adjustment(string culture)
    {
        var start = Home("home-spreadsheets-adjust-start-" + culture);var change = Home("home-spreadsheets-adjust-change-" + culture);
        var conversation = Guid.NewGuid();
        var (first, firstTechnical) = HomeTurn(start, conversation, 1);
        var (result, technical) = HomeTurn(change, conversation, 2, firstTechnical.AttachmentKey);
        return (change, result, technical, (conversation, 1), first.ObservedProposal!, firstTechnical.AttachmentKey!);
    }

    public static void Home_spreadsheet_checks_accept_the_observed_turns()
    {
        foreach (var testCase in HomeCases().Where(item => item.Authoring!.Continues is null))
        {
            var (result, technical) = HomeTurn(testCase, Guid.NewGuid(), 1);
            Require(HomeAccepted(testCase, result, technical));
        }
        foreach (var culture in Cultures)
        {
            var (change, result, technical, conversation, previous, key) = Adjustment(culture);
            Require(HomeAccepted(change, result, technical, conversation, previous, key));
        }
    }

    public static void Home_spreadsheet_checks_reject_another_offer_or_version()
    {
        var testCase = Home("home-spreadsheets-xlsx-catalog-es");
        var (result, technical) = HomeTurn(testCase, Guid.NewGuid(), 1);
        // What Home offered before 6e27fa0b (version 1 and no import) is not the context of these cases.
        Require(!HomeAccepted(testCase, result, technical with { OfferedReferences = HomeStage1 }));
        Require(!HomeAccepted(testCase, result, technical with { OfferedReferences = [..Program.HomeAuthoringCapabilities.Where(reference => reference.Id != SpreadsheetOracle.ProposeImport)] }));
        Require(!HomeAccepted(testCase, result, technical with { OfferedReferences = [..Program.HomeAuthoringCapabilities, new(AuthoringOracle.ProposeCatalog, 1)] }));
        Require(!HomeAccepted(testCase, result, technical with { OfferedReferences = [..Program.HomeAuthoringCapabilities, new(AuthoringOracle.Decline, 1)] }));
        Require(!HomeAccepted(testCase, result with { ObservedProposal = result.ObservedProposal! with { Capability = new(AuthoringOracle.ProposeCatalog, 1) } }, technical));
        Require(!HomeAccepted(testCase, result with { Proposal = result.Proposal! with { Version = 1 } }, technical));
        Require(!HomeAccepted(testCase, result with { Answer = result.Answer! with { Kind = "answer" } }, technical));
        Require(!HomeAccepted(testCase, result, technical with { Stamps = technical.Stamps with { InstructionsVersion = 1 } }));
        Require(!HomeAccepted(testCase, result, technical with { Capabilities = [..technical.Capabilities, new(AuthoringOracle.ProposeCatalog, 2, 64, "rejected")] }));
        Require(HomeAccepted(testCase, result, technical with { Capabilities = [technical.Capabilities[0], new(AuthoringOracle.ProposeCatalog, 2, 64, "rejected"), technical.Capabilities[1]] }));
        Require(!HomeAccepted(testCase, result, technical with { Capabilities = [technical.Capabilities[0], new(AuthoringOracle.ProposeCatalog, 2, 64, "rejected"), technical.Capabilities[1] with { Version = 1 }] }));
        Require(!HomeAccepted(testCase, result with { ObservedProposal = result.ObservedProposal! with { ObservedReads = [] } }, technical));
    }

    public static void Home_spreadsheet_turn_brings_or_inherits_the_file()
    {
        var testCase = Home("home-spreadsheets-import-en");
        var (result, technical) = HomeTurn(testCase, Guid.NewGuid(), 1);
        Require(HomeAccepted(testCase, result, technical));
        // The turn that brings it shows it in its bubble and keeps the copy under the key of its own request.
        Require(!HomeAccepted(testCase, result with { Attachment = null }, technical));
        Require(!HomeAccepted(testCase, result with { Attachment = result.Attachment! with { Name = "otra.xlsx" } }, technical));
        Require(!HomeAccepted(testCase, result with { Attachment = result.Attachment! with { Rows = 8 } }, technical));
        Require(!HomeAccepted(testCase, result, technical with { AttachmentKey = QualificationSpreadsheets.HomeKey(Guid.NewGuid(), StructuredSpreadsheetKind.Xlsx) }));
        Require(!HomeAccepted(testCase, result, technical with { AttachmentKey = QualificationSpreadsheets.ChannelKey(result.RequestId, StructuredSpreadsheetKind.Xlsx) }));
        Require(!HomeAccepted(testCase, result, technical with { AttachmentSha256 = new string('b', 64) }) && !HomeAccepted(testCase, result, technical with { AttachmentKey = null }));
        // The one that inherits shows nothing and keeps the copy of the turn that brought it.
        foreach (var culture in Cultures)
        {
            var (change, changed, changedTechnical, conversation, previous, key) = Adjustment(culture);
            Require(HomeAccepted(change, changed, changedTechnical, conversation, previous, key));
            Require(!HomeAccepted(change, changed with { Attachment = new(change.Spreadsheet!.FileName, "xlsx", 11, change.Spreadsheet.ByteCount) }, changedTechnical, conversation, previous, key));
            Require(!HomeAccepted(change, changed, changedTechnical with { AttachmentKey = QualificationSpreadsheets.HomeKey(changed.RequestId, StructuredSpreadsheetKind.Xlsx) }, conversation, previous, key));
            Require(!HomeAccepted(change, changed, changedTechnical with { AttachmentKey = null, AttachmentSha256 = null, AttachmentSize = null }, conversation, previous, key));
            Require(!HomeAccepted(change, changed, changedTechnical, conversation, previous, null));
        }
    }

    public static void Home_spreadsheet_without_text_asks_without_proposing()
    {
        foreach (var culture in Cultures)
        {
            var testCase = Home("home-spreadsheets-no-text-" + culture);
            var (result, technical) = HomeTurn(testCase, Guid.NewGuid(), 1);
            Require(HomeAccepted(testCase, result, technical) && result.Answer!.Kind == "clarification");
            // The question names nothing: no proposal is shown, observed or even attempted.
            var proposal = new AssistantObservedProposal(result.TurnId, Guid.NewGuid(), new(AuthoringOracle.ProposeCatalog, 2),
                Ideal(Home("home-spreadsheets-xlsx-catalog-" + culture).Authoring!, culture, Sheet("contratistas.xlsx")));
            Require(!HomeAccepted(testCase, result with { ObservedProposal = proposal }, technical));
            Require(!HomeAccepted(testCase, result with { Proposal = new AiAssistantProposalResponse(proposal.Id, AuthoringOracle.ProposeCatalog, 2, JsonSerializer.SerializeToElement(new { })) }, technical));
            Require(!HomeAccepted(testCase, result, technical with { Capabilities = [new(AuthoringOracle.ProposeCatalog, 2, 64, "rejected")] }));
            Require(!HomeAccepted(testCase, result, technical with { Capabilities = [new(SpreadsheetOracle.ProposeImport, 1, 64, "rejected")] }));
            Require(!HomeAccepted(testCase, result with { Attachment = null }, technical));
        }
    }

    public static void Home_spreadsheet_adjustment_keeps_the_conversation_and_name()
    {
        foreach (var culture in Cultures)
        {
            var (change, result, technical, conversation, previous, key) = Adjustment(culture);
            Require(HomeAccepted(change, result, technical, conversation, previous, key));
            Require(!HomeAccepted(change, result, technical, (Guid.NewGuid(), 1), previous, key));
            Require(!HomeAccepted(change, result, technical, null, previous, key));
            Require(!HomeAccepted(change, result, technical, conversation, null, key));
            Require(!HomeAccepted(change, result, technical, conversation, previous with { Payload = ((FormAuthoringMappedCatalogProposal)previous.Payload) with { Name = "Otro catálogo" } }, key));
            // The unchanged start is not the adjusted proposal: the detail and the new field must be there.
            Require(!HomeAccepted(change, result with { ObservedProposal = result.ObservedProposal! with { Payload = previous.Payload } }, technical, conversation, previous, key));
        }
    }

    public static void Spreadsheet_injection_adds_no_proposal_and_keeps_the_destination()
    {
        foreach (var culture in Cultures)
        {
            var testCase = Home("home-spreadsheets-injection-" + culture);
            var (result, technical) = HomeTurn(testCase, Guid.NewGuid(), 1);
            Require(HomeAccepted(testCase, result, technical));
            // No import into the catalog a cell names, before or instead of the catalog asked for, and no other proposal.
            Require(!HomeAccepted(testCase, result, technical with { Capabilities = [..technical.Capabilities, new(SpreadsheetOracle.ProposeImport, 1, 64, "rejected")] }));
            Require(!HomeAccepted(testCase, result, technical with { Capabilities = [new(SpreadsheetOracle.ProposeImport, 1, 64, "proposed"), ..technical.Capabilities] }));
            var imported = new FormAuthoringImportProposal(new(Fixture.Catalog("suppliers").Id, Fixture.Catalog("suppliers").Names[culture], "text"),
                ((FormAuthoringMappedCatalogProposal)result.ObservedProposal!.Payload).Spreadsheet!);
            Require(!HomeAccepted(testCase, result with { ObservedProposal = result.ObservedProposal with { Capability = new(SpreadsheetOracle.ProposeImport, 1), Payload = imported } }, technical));
            Require(!HomeAccepted(testCase, result, technical with { Capabilities = [..technical.Capabilities, new(AuthoringOracle.ProposeForm, 1, 64, "proposed")] }));
            // The notes must be in what the model read: the case proves it ignored them, not that it never saw them.
            Require(!HomeAccepted(testCase, result, technical, sheet: Sheet("contratistas.xlsx")));
            var channel = Channel("channel-spreadsheets-injection-" + culture);
            var (operation, operationTechnical, key) = ChannelTurn(channel, Guid.NewGuid(), 1);
            Require(ChannelAccepted(channel, operation, operationTechnical, null, key));
            Require(!ChannelAccepted(channel, operation, operationTechnical with { Capabilities = [..operationTechnical.Capabilities, new(SpreadsheetOracle.ProposeImport, 1, 64, "rejected")] }, null, key));
            Require(!ChannelAccepted(channel, operation, operationTechnical with { Capabilities = [..operationTechnical.Capabilities, new(AuthoringOracle.Decline, 1, 64, "rejected")] }, null, key));
        }
    }

    // A channel operation as the engine would answer it for the case: the ideal proposal, the sealed copy and the offer with a spreadsheet.
    private static (AssistantOperationResponse Result, TurnMetadata Technical, string OwnKey) ChannelTurn(ChannelCase testCase, Guid conversation, int ordinal,
        string? inheritedKey = null, IReadOnlyList<AssistantCapabilityReference>? offered = null, AssistantCapabilityPayload? payload = null)
    {
        var expected = testCase.Authoring!;var sheet = Sheet(testCase.Spreadsheet!.FileName);
        var turn = Guid.NewGuid();var requestId = Guid.NewGuid();
        payload ??= Ideal(expected, testCase.Culture, sheet);
        var reads = testCase.RequiredTools.Select((tool, index) => tool == "forms.authoring.catalogs"
            ? new AssistantObservedRead(turn, index + 1, new(tool, 1), new FormAuthoringCatalogsPayload(true, [])) : null).OfType<AssistantObservedRead>().ToArray();
        var observed = new AssistantObservedProposal(turn, Guid.NewGuid(), new(expected.Capability!, expected.Version), payload) { ObservedReads = reads };
        var result = new AssistantOperationResponse(requestId, "completed", observed,
            new("gpt-6.1-sol-candidate-1", 1, "capabilities-v1", 1, DateTime.UnixEpoch, "whatsapp-assistant"), null);
        var tools = testCase.RequiredTools.Select(tool => new ObservedCapability(tool, tool == expected.Capability ? expected.Version : 1, 128,
            ProposalIds.Contains(tool) ? "proposed" : "complete")).ToArray();
        var ownKey = QualificationSpreadsheets.ChannelKey(requestId, sheet.Kind);
        var references = offered ?? QualificationChannelContexts.AutomaticReferences;
        var technical = new TurnMetadata(3, tools.Length, 512, null, null, true, null, new("gpt-6.1-sol-candidate-1", 1, "public", "capabilities-v1", 1, "whatsapp-assistant"), tools)
        {
            ConversationId = conversation, Ordinal = ordinal, OfferedReferences = references, OfferedCapabilities = references.Select(reference => reference.Id).ToArray(),
            InteractionMode = AssistantInteractionMode.Assistant, AttachmentKey = inheritedKey ?? ownKey, AttachmentSha256 = testCase.Spreadsheet.Sha256,
            AttachmentSize = testCase.Spreadsheet.ByteCount,
        };
        return (result, technical, ownKey);
    }
    private static bool ChannelAccepted(ChannelCase testCase, AssistantOperationResponse result, TurnMetadata technical, ChannelObservation? previous, string? ownKey)
    {
        var checks = new List<CheckEvidence>();
        ChannelRunner.AddSpreadsheetChecks(checks, testCase, result, technical, Fixture, Sheet(testCase.Spreadsheet!.FileName), ProposalIds, previous, ownKey);
        return checks.Count > 0 && checks.All(check => check.Passed) && checks.Select(check => check.Id).Distinct(StringComparer.Ordinal).Count() == checks.Count;
    }
    private static (ChannelCase Answer, AssistantOperationResponse Result, TurnMetadata Technical, ChannelObservation Previous) Answer(string culture)
    {
        var question = Channel("channel-spreadsheets-question-" + culture);var answer = Channel("channel-spreadsheets-answer-" + culture);
        var conversation = Guid.NewGuid();
        var (asked, askedTechnical, _) = ChannelTurn(question, conversation, 1);
        var previous = new ChannelObservation(asked.RequestId, asked, askedTechnical, 1, [], new(null, null));
        var (result, technical, _) = ChannelTurn(answer, conversation, 2, askedTechnical.AttachmentKey);
        return (answer, result, technical, previous);
    }

    public static void Channel_spreadsheet_checks_accept_the_observed_operations()
    {
        foreach (var testCase in ChannelCases().Where(item => item.Authoring!.Continues is null))
        {
            var (result, technical, key) = ChannelTurn(testCase, Guid.NewGuid(), 1);
            Require(ChannelAccepted(testCase, result, technical, null, key));
        }
        foreach (var culture in Cultures)
        {
            var (answer, result, technical, previous) = Answer(culture);
            Require(ChannelAccepted(answer, result, technical, previous, null));
        }
    }

    public static void Channel_spreadsheet_checks_reject_another_offer_version_or_copy()
    {
        var testCase = Channel("channel-spreadsheets-catalog-es");
        var (result, technical, key) = ChannelTurn(testCase, Guid.NewGuid(), 1);
        // What the automatic conversation offered in Stage 1 (version 1, no import) is not the spreadsheet conversation.
        Require(!ChannelAccepted(testCase, result, technical with { OfferedReferences = AutomaticStage1 }, null, key));
        Require(!ChannelAccepted(testCase, result, technical with { OfferedReferences = [..QualificationChannelContexts.AutomaticReferences.Where(reference => reference.Id != SpreadsheetOracle.ProposeImport)] }, null, key));
        Require(!ChannelAccepted(testCase, result with { Proposal = result.Proposal! with { Capability = new(AuthoringOracle.ProposeCatalog, 1) } }, technical, null, key));
        Require(!ChannelAccepted(testCase, result with { Proposal = null }, technical, null, key) && !ChannelAccepted(testCase, result with { State = "failed" }, technical, null, key));
        // The operation sealed exactly the copy the harness kept, with the fixture's size and SHA-256.
        Require(!ChannelAccepted(testCase, result, technical with { AttachmentKey = null, AttachmentSha256 = null, AttachmentSize = null }, null, key));
        Require(!ChannelAccepted(testCase, result, technical with { AttachmentKey = QualificationSpreadsheets.ChannelKey(Guid.NewGuid(), StructuredSpreadsheetKind.Xlsx) }, null, key));
        Require(!ChannelAccepted(testCase, result, technical with { AttachmentSize = 1 }, null, key) && !ChannelAccepted(testCase, result, technical, null, null));
        Require(!ChannelAccepted(testCase, result, technical with { Capabilities = [..technical.Capabilities, new(AuthoringOracle.ProposeForm, 1, 64, "proposed")] }, null, key));
    }

    public static void Channel_spreadsheet_answer_continues_the_question_with_its_spreadsheet()
    {
        foreach (var culture in Cultures)
        {
            var (answer, result, technical, previous) = Answer(culture);
            Require(ChannelAccepted(answer, result, technical, previous, null));
            Require(!ChannelAccepted(answer, result, technical, null, null));
            Require(!ChannelAccepted(answer, result, technical with { ConversationId = Guid.NewGuid() }, previous, null));
            Require(!ChannelAccepted(answer, result, technical with { Ordinal = 3 }, previous, null));
            // It inherits the question's spreadsheet: the same copy, never one of its own or none.
            Require(!ChannelAccepted(answer, result, technical with { AttachmentKey = QualificationSpreadsheets.ChannelKey(result.RequestId, StructuredSpreadsheetKind.Csv) }, previous, null));
            Require(!ChannelAccepted(answer, result, technical with { AttachmentKey = null, AttachmentSha256 = null, AttachmentSize = null }, previous, null));
            // The question it answers asked what to build; another reason or another catalog's proposal is not that question.
            Require(!ChannelAccepted(answer, result, technical, previous with { Result = previous.Result with { Proposal = previous.Result.Proposal! with {
                Payload = new FormAuthoringDecline("noPermission", null, null, "") } } }, null));
            Require(!ChannelAccepted(answer, result, technical, previous with { Result = previous.Result with { Proposal = previous.Result.Proposal! with {
                Payload = Ideal(Channel("channel-spreadsheets-catalog-" + culture).Authoring!, culture, Sheet("contratistas.xlsx")) } } }, null));
        }
    }

    public static void Channel_spreadsheet_question_names_nothing()
    {
        foreach (var culture in Cultures)
        {
            var question = Channel("channel-spreadsheets-question-" + culture);
            var (result, technical, key) = ChannelTurn(question, Guid.NewGuid(), 1);
            Require(ChannelAccepted(question, result, technical, null, key));
            foreach (var named in new[] { new FormAuthoringDecline("unclear", null, Fixture.Catalog("suppliers").Id, "Proveedores"),
                new FormAuthoringDecline("unclear", Fixture.Form("visit").DefinitionId, null, ""), new FormAuthoringDecline("unclear", null, null, "Insumos"),
                new FormAuthoringDecline("catalogExists", null, Fixture.Catalog("suppliers").Id, "Proveedores"), new FormAuthoringDecline("noPermission", null, null, "") })
                Require(!ChannelAccepted(question, result with { Proposal = result.Proposal! with { Payload = named } }, technical, null, key));
            var mapped = Ideal(Channel("channel-spreadsheets-answer-" + culture).Authoring!, culture, Sheet("insumos.csv"));
            Require(!ChannelAccepted(question, result with { Proposal = result.Proposal! with { Capability = new(AuthoringOracle.ProposeCatalog, 2), Payload = mapped } }, technical, null, key));
        }
    }

    public static void Channel_spreadsheet_ledger_resumes_its_context() => LedgerAsync().GetAwaiter().GetResult();
    private static async Task LedgerAsync()
    {
        var person = Guid.NewGuid();
        var entry = new QualificationLedgerEntry("channel-spreadsheets-answer-es", Guid.NewGuid(), person, null, null, "before-submit")
            { OperationKind = "channel", ChannelId = "whatsapp-assistant", InteractionMode = AssistantInteractionMode.Assistant, Culture = "es",
              SubjectUserId = person, ChannelKind = "author", PreviousRequestId = Guid.NewGuid() };
        var resumed = QualificationChannelContexts.Resume(entry);
        Require(resumed is { ChannelId: "whatsapp-assistant", InteractionMode: AssistantInteractionMode.Assistant, SourceRecordId: null } && resumed.ActorUserId == person
            && resumed.SubjectUserId == person && resumed.AllowedCapabilities.SequenceEqual(QualificationChannelContexts.AutomaticReferences));
        Require(Throws(() => QualificationChannelContexts.Resume(entry with { ChannelKind = "manual" }))
            && Throws(() => QualificationChannelContexts.Resume(entry with { ChannelKind = "sheet-author" })));
        var directory = Path.Combine(AppContext.BaseDirectory, "offline-ledger-" + Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var ledger = new SafeRunLedger(new("offline_run", AppContext.BaseDirectory, directory, "unused", "unused", "unused"));
            await ledger.SaveAsync(entry, CancellationToken.None);await ledger.SaveAsync(entry with { Phase = "accepted" }, CancellationToken.None);
            foreach (var changed in new[] { entry with { ChannelKind = "auto" }, entry with { PreviousRequestId = null } })
            {
                var rejected = false;
                try { await ledger.SaveAsync(changed, CancellationToken.None); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    // The engine accepts ten operations a day per person, channel and kind (interpretation or audio), counted with the runners' own
    // choice of people: in Home, the authors in turns and a follow-up with its start's; by WhatsApp, the author or the one who dictates.
    public static void Spreadsheet_sets_keep_each_person_within_the_daily_quota()
    {
        static QualificationActor Person(string culture) => new(Guid.NewGuid(), "offline", culture, "fictitious");
        QualificationActor[] Pair() => [Person("es"), Person("en")];
        var actors = new QualificationFixture([], "Supervisor") { Authors = [..Pair(), ..Pair()], ChannelAuthors = Pair(), VoiceAuthors = Pair() };
        var quota = new AiAssistantLimits().ActorDailyQuota;
        var cases = HomeCases();var conversations = new Dictionary<string, QualificationActor>(StringComparer.Ordinal);
        var home = new List<(Guid, string)>();
        foreach (var testCase in cases)
        {
            var actor = testCase.ConversationKey is { } key && conversations.TryGetValue(key, out var started) ? started : Program.HomeActor(cases, testCase, actors);
            if (testCase.ConversationKey is { } saved) conversations[saved] = actor;
            home.Add((actor.UserId, "home"));
            if (testCase.Audio is not null) home.Add((actor.UserId, "home-audio"));
        }
        Require(home.Count == 20 && home.GroupBy(operation => operation).All(group => group.Count() <= quota));
        var channel = ChannelCases().SelectMany(testCase =>
        {
            var (actor, subject) = ChannelRunner.People(testCase, actors);
            var interpretation = (actor.UserId, QualificationChannelContexts.Create(actor, subject.UserId, testCase.ActorKind).ChannelId, false);
            (Guid, string, bool)[] operations = testCase.Audio is null ? [interpretation] : [interpretation, (actor.UserId, "whatsapp-assistant", true)];
            return operations;
        }).ToArray();
        Require(channel.Length == 14 && channel.GroupBy(operation => operation).All(group => group.Count() <= quota)
            && ChannelCases().Where(testCase => testCase.Audio is not null).All(testCase => ChannelRunner.People(testCase, actors).Actor.UserId
                == actors.VoiceAuthors.Single(person => person.Culture == testCase.Culture).UserId));
    }

    public static void Spreadsheet_dictation_fixtures_say_the_request_in_both_formats()
    {
        var home = HomeCases().Where(testCase => testCase.Audio is not null).ToArray();var channel = ChannelCases().Where(testCase => testCase.Audio is not null).ToArray();
        Require(home.Length == 2 && channel.Length == 2);
        foreach (var culture in Cultures)
        {
            var webm = home.Single(testCase => testCase.Culture == culture);var ogg = channel.Single(testCase => testCase.Culture == culture);
            var (browser, note) = (webm.Audio!, ogg.Audio!);
            Require(AudioFixtureRules.IsValidBrowser(browser) && browser.ContentType == "audio/webm" && AudioFixtureRules.IsValidVoiceNote(note)
                && browser.FileName == "spreadsheet-" + culture + ".webm" && note.FileName == "spreadsheet-" + culture + ".ogg");
            var browserBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "audio", browser.FileName));
            var noteBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "audio", note.FileName));
            Require(browserBytes.LongLength == browser.ByteCount && EvidenceWriter.HashPublicBytes(browserBytes) == browser.Sha256
                && AudioFixtureRules.DetectBrowser(browserBytes) == "audio/webm" && browserBytes.LongLength <= SubmitAiAssistantAudioRequest.MaximumBytes);
            Require(noteBytes.LongLength == note.ByteCount && EvidenceWriter.HashPublicBytes(noteBytes) == note.Sha256 && AudioFixtureRules.IsVoiceNote(noteBytes));
            // The same synthesized speech in both formats, asking for what the cases expect of the list.
            Require(browser.ExpectedTranscript == note.ExpectedTranscript && browser.RequiredWords.SequenceEqual(note.RequiredWords)
                && JsonSerializer.Serialize(webm.Authoring) == JsonSerializer.Serialize(ogg.Authoring) && webm.Spreadsheet == ogg.Spreadsheet
                && webm.Spreadsheet is { FileName: "tipos-de-tarea.xlsx", Inherited: false });
            var expected = webm.Authoring!;var transcript = browser.ExpectedTranscript;
            Require(AuthoringOracle.ContainsPhrase(transcript, expected.Name!) && AuthoringOracle.ContainsPhrase(transcript, culture == "es" ? "misma columna" : "same column")
                && AuthoringOracle.ContainsPhrase(transcript, culture == "es" ? "encabezado" : "header") && expected.Mapping is { HasHeaders: true, Code: "A", Option: "A" }
                && AudioFixtureRules.Matches(transcript, transcript.ToUpperInvariant().Replace(",", "", StringComparison.Ordinal), browser.RequiredWords));
        }
    }

    public static void Private_spreadsheet_copies_are_write_once_with_their_media_type() => PrivateAsync().GetAwaiter().GetResult();
    private static async Task PrivateAsync()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "offline-spreadsheets-" + Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var store = new QualificationPrivateFiles(directory);
            var home = QualificationSpreadsheets.HomeKey(Guid.NewGuid(), StructuredSpreadsheetKind.Xlsx);var channel = QualificationSpreadsheets.ChannelKey(Guid.NewGuid(), StructuredSpreadsheetKind.Csv);
            using (var first = new MemoryStream(Encoding.UTF8.GetBytes("public fixture one"))) Require(await store.WriteOnceAsync(home, first, QualificationSpreadsheets.XlsxMediaType, CancellationToken.None));
            using (var second = new MemoryStream(Encoding.UTF8.GetBytes("public fixture two"))) Require(!await store.WriteOnceAsync(home, second, QualificationSpreadsheets.XlsxMediaType, CancellationToken.None));
            using (var third = new MemoryStream(Encoding.UTF8.GetBytes("A;B\n"))) Require(await store.WriteOnceAsync(channel, third, QualificationSpreadsheets.CsvMediaType, CancellationToken.None));
            await using (var read = await new QualificationPrivateFiles(directory).OpenReadAsync(home, CancellationToken.None))
            {
                Require(read is not null);
                using var reader = new StreamReader(read!);Require(await reader.ReadToEndAsync() == "public fixture one");
            }
            foreach (var (key, type) in new[] { (home.Replace(".xlsx", ".csv", StringComparison.Ordinal), QualificationSpreadsheets.XlsxMediaType),
                (channel, QualificationSpreadsheets.XlsxMediaType), ("ai/assistant/attachments/../outside.xlsx", QualificationSpreadsheets.XlsxMediaType),
                ("whatsapp/documents/" + Guid.NewGuid().ToString("N") + ".xlsx", QualificationSpreadsheets.XlsxMediaType),
                ("ai/assistant/attachments/" + Guid.NewGuid().ToString("N") + ".xls", "application/vnd.ms-excel"),
                ("qualification/attachments/" + Guid.NewGuid().ToString("N") + ".xlsx", "text/csv") })
            {
                var rejected = false;
                try { using var content = new MemoryStream([1]); await store.WriteOnceAsync(key, content, type, CancellationToken.None); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected);
            }
            Require(QualificationSpreadsheets.IsKey(home) && QualificationSpreadsheets.IsKey(channel) && !QualificationSpreadsheets.IsKey(home.ToUpperInvariant()));
        }
        finally { Directory.Delete(directory, true); }
    }

    public static void Spreadsheet_case_sets_list_their_own_fixtures()
    {
        Require(SpreadsheetSets.All(set => Program.CaseSets.Contains(set, StringComparer.Ordinal)));
        // The spreadsheet sets keep their own cases: no Stage 1 authoring case, no channel-stage5 case and no other set's identity.
        var others = Cases<HomeCase>("home-authoring-cases.json").Select(testCase => testCase.Id).Concat(Cases<ChannelCase>("channel-authoring-cases.json").Select(testCase => testCase.Id))
            .Concat(Cases<HomeCase>("home-cases.json").Select(testCase => testCase.Id)).Concat(Cases<ChannelCase>("channel-cases.json").Select(testCase => testCase.Id)).ToHashSet(StringComparer.Ordinal);
        Require(HomeCases().Select(testCase => testCase.Id).Concat(ChannelCases().Select(testCase => testCase.Id)).All(id => !others.Contains(id))
            && Program.SelectChannelCases("channel-spreadsheets", ChannelCases()).Length == 12 && Program.SelectChannelCases("ia-spreadsheets", ChannelCases()).Length == 12);
        // Their names never collide with the seeded fixture, the import catalog included.
        var names = QualificationFormSeed.FormNames.SelectMany(name => new[] { name.Spanish, name.English })
            .Concat(QualificationFormSeed.CatalogNames.Append(QualificationAuthoringSeed.ImportCatalog).SelectMany(name => new[] { name.Spanish, name.English })).ToArray();
        Require(HomeCases().Select(testCase => testCase.Authoring!.Name).Concat(ChannelCases().Select(testCase => testCase.Authoring!.Name)).OfType<string>()
            .All(name => names.All(known => AuthoringOracle.Canonical(known) != AuthoringOracle.Canonical(name))));
        // The Stage 1 authoring sets never request the import catalog's name, since they never seed it.
        Require(Cases<HomeCase>("home-authoring-cases.json").Select(testCase => testCase.Authoring?.Name).Concat(Cases<ChannelCase>("channel-authoring-cases.json").Select(testCase => testCase.Authoring?.Name))
            .OfType<string>().All(name => AuthoringOracle.Canonical(name) is not ("proveedores" or "suppliers")));
    }

    private static void Require(bool valid) { if (!valid) throw new InvalidOperationException("Offline spreadsheet check failed."); }
}
