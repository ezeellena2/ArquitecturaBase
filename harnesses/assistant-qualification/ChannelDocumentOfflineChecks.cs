using System.Globalization;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AssistantQualification;

// channel-documents (task 17a): the cases against the manifest, the document-reading cases and the files, what they cover, the spreadsheets
// through the real loading port, the contexts the channel builds, the daily quotas, the ledgers and every oracle with its positives and
// negatives. No provider, secret store or database.
internal static class ChannelDocumentOfflineChecks
{
    public static readonly Action[] All = [Channel_document_cases_agree_with_the_manifest_the_readings_and_the_files, Channel_document_cases_cover_what_the_channel_takes,
        Channel_document_inventory_rejects_a_changed_unpaired_or_orphan_case, Channel_document_case_file_pins_the_manifest_and_the_readings,
        Channel_document_spreadsheets_prepare_as_their_cases_expect, Channel_document_contexts_follow_the_access_policy,
        Channel_document_context_check_follows_what_the_engine_seals,
        Channel_document_set_keeps_each_person_within_the_daily_quotas, Channel_document_ledgers_resume_their_exact_context,
        Channel_document_alone_text_is_the_server_text, Channel_document_oracle_accepts_the_ideal_proposals,
        Channel_document_oracle_rejects_a_wrong_form_digit_decision_or_obeyed_instruction, Channel_sheet_oracle_rejects_a_wrong_row_column_doubt_or_focus,
        Channel_documents_set_is_listed_apart_from_the_other_sets];

    private static readonly string[] Cultures = ["es", "en"];
    private static readonly Guid Subject = Guid.NewGuid();
    private static readonly Lazy<VerifiedChannelDocumentSet> Set = new(() => QualificationChannelDocuments.LoadVerifiedAsync(CancellationToken.None).GetAwaiter().GetResult());
    private static ChannelCase[] Cases() => Set.Value.Cases;
    private static ChannelCase Case(string id) => Cases().Single(testCase => testCase.Id == id);
    private static readonly QualificationChannelFixture Fixture = new([..QualificationChannelDocuments.ReadingForms.Select(form =>
        new QualificationForm(Guid.NewGuid(), Guid.NewGuid(), form.Alias, form.Fields.ToDictionary(field => field.Key, _ => Guid.NewGuid(), StringComparer.Ordinal)))], [], []);
    private static readonly IReadOnlyDictionary<string, string> NoColumns = new Dictionary<string, string>();

    // The real owner port without a database, as the other spreadsheet controls build it.
    private sealed class NoPermissions : IPermissionService
    {
        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) => Task.FromResult(false);
    }
    private static readonly Lazy<IReadOnlyDictionary<string, StructuredSpreadsheet>> Prepared = new(() =>
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddScoped<QualificationCurrentUser>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<QualificationCurrentUser>());
        services.AddSingleton<IPermissionService>(new NoPermissions());
        services.AddBusinessFormsApplication().AddBusinessFormsInfrastructure();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<IStructuredAttachmentReader>();
        return Cases().Select(testCase => testCase.Spreadsheet).OfType<SpreadsheetFixture>().DistinctBy(sheet => sheet.FileName).ToDictionary(sheet => sheet.FileName,
            sheet => port.PrepareRowsAsync(QualificationSpreadsheets.ReadAsync(sheet, CancellationToken.None).GetAwaiter().GetResult(), sheet.FileName,
                CancellationToken.None).GetAwaiter().GetResult().Spreadsheet ?? throw new InvalidOperationException("Offline check failed."), StringComparer.Ordinal);
    });

    public static void Channel_document_cases_agree_with_the_manifest_the_readings_and_the_files()
    {
        var set = Set.Value;
        Require(set.Cases.Length == 28 && QualificationChannelDocuments.Inventory(set.Cases, set.Manifest, set.Readings));
        // Every reading expects what document-reading expects of the same file in the same culture.
        Require(Cases().Where(QualificationChannelDocuments.IsDocument).All(testCase => set.Reading(testCase).Culture == testCase.Culture
            && set.Document(testCase).Sha256 == testCase.Document!.Sha256));
    }

    public static void Channel_document_cases_cover_what_the_channel_takes()
    {
        bool Has(Func<ChannelCase, bool> predicate) => Cultures.All(culture => Cases().Count(testCase => testCase.Culture == culture && predicate(testCase)) == 1);
        string Kind(ChannelCase testCase) => QualificationChannelDocuments.Kind(testCase);
        // A doubt rests on a receipt whose concept leans to neither form («Varios»); the «No lo reconoce» spreadsheet comes alone, the only
        // way its turn reaches the list that the chosen form continues.
        bool Neutral(ChannelCase item) => item.Document is not null && Set.Value.Document(item).Text.SkipWhile(line => line != "Concepto").Skip(1).FirstOrDefault() == "Varios";
        Require(Has(item => item.Id.Contains("-caption-", StringComparison.Ordinal) && Kind(item) == "loader" && item.Loading!.Decision == "sure")
            && Has(item => item.Id.Contains("-text-", StringComparison.Ordinal) && Kind(item) == "loader" && item.Loading!.Decision == "sure" && item.Loading.Forms is ["collection"])
            && Has(item => item.Audio is not null && item.Question.Length == 0 && Kind(item) == "loader" && item.Loading!.Decision == "sure")
            && Has(item => Kind(item) == "loader-alone" && item.Loading!.Decision == "doubt" && Neutral(item))
            && Has(item => Kind(item) == "loader" && item.Loading!.Decision == "doubt" && Neutral(item))
            && Has(item => Kind(item) == "loader-alone" && item.Loading!.Decision == "unknown" && item.Document!.FileName == "planilla-en-papel.pdf")
            && Has(item => Set.Value.Document(item.Document is null ? Cases()[0] : item).Instruction is not null && item.Document is not null && item.Loading!.Decision == "sure")
            && Has(item => item.Document?.FileName == "foto-movida.jpg" && item.Loading!.Decision == "unreadable")
            && Has(item => item.Spreadsheet is not null && item.Loading!.Rows is [{ Dubious: null }] && item.Loading.Note is null && item.Continues is null)
            && Has(item => item.Loading!.Rows is { Length: 2 })
            && Has(item => item.Loading!.Rows is [{ Dubious: not null }])
            && Has(item => item.Loading!.Note is not null)
            && Has(item => Kind(item) == "sheet-loader-alone" && item.Loading!.Decision == "unknown")
            && Has(item => item.Focus is not null && Kind(item) == "sheet-loader-focused" && item.Loading!.Decision == "rows"));
        // Both «Cómo reconocerlo» list the same printed data and differ only in who paid whom.
        var data = QualificationChannelDocuments.ReadingForms.Select(form => form.Recognition[form.Recognition.IndexOf(": dice", StringComparison.Ordinal)..]).ToArray();
        Require(data is [var collection, var payment] && collection == QualificationChannelDocuments.RecognitionData("el cliente")
            && payment == QualificationChannelDocuments.RecognitionData("el proveedor"));
    }

    public static void Channel_document_inventory_rejects_a_changed_unpaired_or_orphan_case()
    {
        var set = Set.Value;
        bool Holds(IEnumerable<ChannelCase> changed) => QualificationChannelDocuments.Inventory([..changed], set.Manifest, set.Readings);
        ChannelCase[] Changed(string id, Func<ChannelCase, ChannelCase> change) => [..Cases().Select(item => item.Id == id ? change(item) : item)];
        var caption = Case("channel-document-caption-es");
        Require(Holds(Cases())
            && !Holds(Changed(caption.Id, item => item with { Document = item.Document! with { ByteCount = item.Document.ByteCount + 1 } }))
            && !Holds(Changed(caption.Id, item => item with { Loading = item.Loading! with { Facts = new Dictionary<string, string> { ["amount-9"] = "amount" } } }))
            && !Holds(Changed(caption.Id, item => item with { Loading = item.Loading! with { Forms = ["unknown-form"] } }))
            && !Holds(Changed(caption.Id, item => item with { Loading = item.Loading! with { Decision = "doubt" } }))
            && !Holds(Changed(caption.Id, item => item with { Document = item.Document! with { FileName = "orden-de-pago.docx" } }))
            && !Holds(Cases().Where(item => item.Id != "channel-document-caption-en"))
            && !Holds(Cases().Where(item => item.Id != "channel-sheet-unknown-es"))
            && !Holds(Cases().Select(item => item.Id.StartsWith("channel-sheet-unknown-", StringComparison.Ordinal) ? item with { Question = "Cargá esta planilla." } : item))
            && !Holds(Changed("channel-sheet-chosen-es", item => item with { Focus = "collection" }))
            && !Holds(Changed("channel-sheet-dubious-es", item => item with { Loading = item.Loading! with { Rows = [item.Loading.Rows![0] with { Columns =
                new Dictionary<string, string> { ["date"] = "A", ["nonexistent"] = "B" } }] } })));
    }

    public static void Channel_document_case_file_pins_the_manifest_and_the_readings()
    {
        var file = QualificationChannelDocuments.Load(File.ReadAllBytes(QualificationChannelDocuments.CasePath));
        var manifest = File.ReadAllBytes(QualificationDocuments.ManifestPath);
        var readings = File.ReadAllBytes(QualificationHomeDocuments.ReadingsPath);
        var set = Set.Value;
        var crlf = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(readings).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
        Require(QualificationChannelDocuments.Verified(file, manifest, readings, set.Readings, set.Manifest)
            && QualificationChannelDocuments.Verified(file, manifest, crlf, set.Readings, set.Manifest)
            && !QualificationChannelDocuments.Verified(file, [..manifest, (byte)' '], readings, set.Readings, set.Manifest)
            && !QualificationChannelDocuments.Verified(file, manifest, [..readings, (byte)'x'], set.Readings, set.Manifest)
            && !QualificationChannelDocuments.Verified(file with { ManifestSha256 = new string('0', 64) }, manifest, readings, set.Readings, set.Manifest));
    }

    public static void Channel_document_spreadsheets_prepare_as_their_cases_expect()
    {
        foreach (var testCase in Cases().Where(item => item.Spreadsheet is not null))
        {
            var sheet = Prepared.Value[testCase.Spreadsheet!.FileName];
            var data = sheet.Rows.Where(row => row.Number > 1).Select(row => row.Number).ToArray();
            var header = sheet.Rows.SingleOrDefault(row => row.Number == 1);
            var wanted = testCase.Loading!.Rows ?? [];
            // A sample to load: every row with its sheet (empty in a CSV), the headers in row 1 and the data rows after them, all of them seen.
            Require(sheet.Rows.All(row => row.Sheet is not null) && header is not null && data.SequenceEqual(Enumerable.Range(2, data.Length))
                && data.Length is >= 1 and <= StructuredAttachmentLimits.MaximumLoadingRows && sheet.Kind == StructuredSpreadsheetKind.Csv
                && (wanted.Length == 0 || wanted.SelectMany(item => item.Rows).Order().SequenceEqual(data))
                && wanted.All(item => item.Columns.Concat(item.Dubious ?? NoColumns).All(column => header.Cells.ContainsKey(column.Value))));
            if (testCase.Loading.Note is { } note)
                Require(note.Length <= 80 && sheet.Rows.Sum(row => row.Cells.Values.Count(value => value == note)) == 1
                    && ChannelDocumentOracle.RowsChecks(testCase, Context(testCase), IdealRows(testCase, sheet), sheet, Fixture, Subject).Single(item => item.Id == "untrusted-cell-note-in-prepared-sheet").Passed);
        }
    }

    public static void Channel_document_contexts_follow_the_access_policy()
    {
        var person = new QualificationActor(Guid.NewGuid(), "offline", "es", "QualificationSubject");
        var focus = Guid.NewGuid();
        AssistantChannelContext Of(string kind) => QualificationChannelContexts.Create(person, person.UserId, kind, null, kind == "sheet-loader-focused" ? focus : null);
        var automatic = QualificationChannelContexts.Create(person, person.UserId, "auto");
        var document = Of("loader");
        var alone = Of("loader-alone");
        var sheet = Of("sheet-loader");
        var sheetAlone = Of("sheet-loader-alone");
        var focused = Of("sheet-loader-focused");
        // WhatsAppAssistantFileCatalogTests: 13 definitions with a document and its request, 3 alone; 15 with a spreadsheet to load and its
        // request, 3 alone; all within the sixteen, each closing on one proposal.
        Require(QualificationChannelDocuments.Definitions(document, true) == 13 && QualificationChannelDocuments.Definitions(alone, true) == 3
            && QualificationChannelDocuments.Definitions(sheet, false) == 15 && QualificationChannelDocuments.Definitions(sheetAlone, false) == 3
            && QualificationChannelDocuments.Definitions(focused, false) == 3
            && QualificationChannelDocuments.MaximumDefinitions >= 15
            && new[] { document, alone, sheet, sheetAlone, focused }.All(context => context is { ChannelId: "whatsapp-assistant", InteractionMode: AssistantInteractionMode.Assistant, SourceRecordId: null }
                && context.ActorUserId == person.UserId && context.SubjectUserId == person.UserId && context.AllowedTerminals.SequenceEqual([new AssistantTerminalReference("assistant_proposal", 1)])));
        Require(document.AllowedCapabilities.SequenceEqual([..automatic.AllowedCapabilities, ..QualificationChannelDocuments.DocumentCapabilities])
            && alone.AllowedCapabilities.SequenceEqual(QualificationChannelDocuments.DocumentCapabilities) && alone.PreloadedCapabilities is null
            && sheet.AllowedCapabilities.Count == automatic.AllowedCapabilities.Count - 1 && sheet.AllowedCapabilities.TakeLast(2).SequenceEqual(QualificationChannelDocuments.SpreadsheetCapabilities)
            && !sheet.AllowedCapabilities.Any(item => item.Id is "forms.assistant-candidates" or "forms.propose-registration" or "forms.clarify-registration")
            && sheetAlone.AllowedCapabilities.SequenceEqual(QualificationChannelDocuments.SpreadsheetCapabilities)
            && focused.FocusDefinitionId == focus && new[] { document, alone, sheet, sheetAlone }.All(context => context.FocusDefinitionId is null));
        Require(Throws(() => QualificationChannelContexts.Create(person, Guid.NewGuid(), "loader"))
            && Throws(() => QualificationChannelContexts.Create(person, person.UserId, "sheet-loader-focused"))
            && Throws(() => QualificationChannelContexts.Create(person, person.UserId, "loader", null, focus))
            && Throws(() => QualificationChannelContexts.Create(person, person.UserId, "auto", null, focus)));
    }

    // The engine seals what it offers of the channel's context (AiAssistantCapabilityCatalogPolicy.BuildAsync): each capability and preload whose
    // definition the turn's file admits and whose permissions the Actor and the Subject hold, in the context's order, and no capability that
    // drafts text with the set's candidate (AiAssistantAccessGuard.CaptureProfileAsync); QualificationChannelDocuments.Sealed, on definitions made
    // here. The check takes exactly that, with the loading capabilities, and the terminals, the focus, the mode and the Subject as the channel
    // built them.
    public static void Channel_document_context_check_follows_what_the_engine_seals()
    {
        var person = new QualificationActor(Guid.NewGuid(), "offline", "es", "QualificationSubject");
        static AssistantCapabilityDefinition Definition(AssistantCapabilityReference reference, AssistantCapabilityAttachment attachment = AssistantCapabilityAttachment.Any,
            string[]? actor = null, string[]? subject = null, bool drafts = false) =>
            new(reference.Id, reference.Version, AssistantCapabilityKind.Read, "Fictitious capability.", "{}", "{}", new(actor ?? [], subject ?? []), new(1, 1, 1, 1))
            { Attachment = attachment, DraftsText = drafts };
        AssistantCapabilityReference spreadsheetOnly = new(SpreadsheetOracle.ProposeImport, 1), noDocument = new("forms.propose-registration", 1),
            guarded = new("forms.authoring.forms", 1), forSubject = new("forms.catalog-filters.metadata", 1), drafting = new("whatsapp.no-reply", 1),
            candidates = new(QualificationChannelDocuments.Candidates, 1), entry = new(QualificationChannelDocuments.ProposeEntry, 1),
            preloadAny = new("qualification.preload-any", 1), preloadNoDocument = new("qualification.preload-no-document", 1),
            preloadDrafting = new("qualification.preload-drafting", 1);
        var loader = QualificationChannelContexts.Create(person, person.UserId, "loader")
            with { PreloadedCapabilities = [preloadAny, preloadNoDocument, preloadDrafting] };
        // As the catalog registers them, in another order than the context's.
        AssistantCapabilityDefinition[] registered = [..loader.AllowedCapabilities.Concat(loader.PreloadedCapabilities ?? []).Select(reference =>
            reference == spreadsheetOnly ? Definition(reference, AssistantCapabilityAttachment.Spreadsheet)
            : reference == noDocument || reference == preloadNoDocument ? Definition(reference, AssistantCapabilityAttachment.NoDocument)
            : reference == guarded ? Definition(reference, actor: ["forms.design"])
            : reference == forSubject ? Definition(reference, subject: ["forms.records.read"])
            : reference == drafting || reference == preloadDrafting ? Definition(reference, drafts: true)
            : reference == entry ? Definition(reference, AssistantCapabilityAttachment.Document, ["forms.records.create"])
            : reference == candidates ? Definition(reference, AssistantCapabilityAttachment.File, ["forms.records.create"])
            : Definition(reference)).Reverse()];
        string[] granted = ["forms.records.create"], reads = ["forms.records.read"];
        var (offered, preloaded) = QualificationChannelDocuments.Sealed(loader, registered, AssistantAttachmentKind.Document, granted, granted);
        Require(offered.SequenceEqual(loader.AllowedCapabilities.Where(item => item != spreadsheetOnly && item != noDocument && item != guarded
                && item != forSubject && item != drafting)) && preloaded.SequenceEqual([preloadAny, preloadDrafting]));
        // With a spreadsheet the document's proposal goes and the spreadsheet's and the one that never goes with a document stay. The Actor's and
        // the Subject's permissions count apart, a capability needs its exact version and one the catalog does not register stops the run.
        var sheet = QualificationChannelDocuments.Sealed(loader, registered, AssistantAttachmentKind.Spreadsheet, granted, reads).Offered;
        var crossed = QualificationChannelDocuments.Sealed(loader, registered, AssistantAttachmentKind.Spreadsheet, reads, granted).Offered;
        Require(sheet.Contains(spreadsheetOnly) && sheet.Contains(noDocument) && sheet.Contains(forSubject) && sheet.Contains(candidates)
            && !sheet.Contains(entry) && !crossed.Contains(forSubject) && !crossed.Contains(candidates)
            && Throws(() => QualificationChannelDocuments.Sealed(loader with
                { AllowedCapabilities = [..loader.AllowedCapabilities.Where(item => item != entry), new(QualificationChannelDocuments.ProposeEntry, 2)] },
                registered, AssistantAttachmentKind.Document, granted, granted))
            && Throws(() => QualificationChannelDocuments.Sealed(loader with { AllowedCapabilities = [..loader.AllowedCapabilities, new("forms.unknown", 1)] },
                registered, AssistantAttachmentKind.Document, granted, granted)));
        static bool Passes(AssistantChannelContext expected, AssistantCapabilityReference[] offered, AssistantCapabilityReference[] preloaded, bool document,
            TurnMetadata technical) => ChannelDocumentOracle.ContextChecks(expected, offered, preloaded, document, technical)
                .Single(check => check.Id == "loading-context-matches-the-channel").Passed;
        static TurnMetadata Turn(AssistantChannelContext context, IReadOnlyList<AssistantCapabilityReference> offered,
            IReadOnlyList<AssistantCapabilityReference> preloaded) =>
            new(2, 1, 4000, null, null, true, null, new("gpt-6.1-sol-candidate-1", 1, "hash", "capabilities-v1", 1, "whatsapp-assistant"), [])
            {
                InteractionMode = context.InteractionMode, SubjectUserId = context.SubjectUserId, OfferedReferences = offered,
                PreloadedReferences = preloaded, Terminals = context.AllowedTerminals, FocusDefinitionId = context.FocusDefinitionId,
            };
        var turn = Turn(loader, offered, preloaded);
        Require(Passes(loader, offered, preloaded, true, turn));
        // Never the context as it was sent, an extra, a missing or a moved capability, other preloads, focus, Subject, terminals or mode.
        Require(!Passes(loader, offered, preloaded, true, Turn(loader, loader.AllowedCapabilities, preloaded))
            && !Passes(loader, offered, preloaded, true, Turn(loader, [..offered, spreadsheetOnly], preloaded))
            && !Passes(loader, offered, preloaded, true, Turn(loader, [..offered.Skip(1)], preloaded))
            && !Passes(loader, offered, preloaded, true, Turn(loader, [..offered.Reverse()], preloaded))
            && !Passes(loader, offered, preloaded, true, Turn(loader, offered, []))
            && !Passes(loader, offered, preloaded, true, turn with { FocusDefinitionId = Guid.NewGuid() })
            && !Passes(loader, offered, preloaded, true, turn with { SubjectUserId = Guid.NewGuid() })
            && !Passes(loader, offered, preloaded, true, turn with { Terminals = [] })
            && !Passes(loader, offered, preloaded, true, turn with { InteractionMode = AssistantInteractionMode.Manual }));
        // A loader without the permission to load seals what the engine computes and still qualifies nothing.
        var bare = QualificationChannelDocuments.Sealed(loader, registered, AssistantAttachmentKind.Document, [], []).Offered;
        Require(!Passes(loader, bare, preloaded, true, Turn(loader, bare, preloaded)));
        // A spreadsheet focused on the chosen form offers its own loading capabilities, never a document's.
        var focused = QualificationChannelContexts.Create(person, person.UserId, "sheet-loader-focused", null, Guid.NewGuid());
        AssistantCapabilityReference[] rows = [..QualificationChannelDocuments.SpreadsheetCapabilities];
        Require(Passes(focused, rows, [], false, Turn(focused, rows, [])) && !Passes(focused, rows, [], true, Turn(focused, rows, [])));
    }

    public static void Channel_document_set_keeps_each_person_within_the_daily_quotas()
    {
        static QualificationActor Person(string culture, string role) => new(Guid.NewGuid(), "offline", culture, role);
        var fixture = new QualificationFixture([], "Supervisor")
        {
            Subjects = [Person("es", "QualificationSubject"), Person("en", "QualificationSubject")],
            SheetLoaders = [Person("es", "QualificationSubject"), Person("en", "QualificationSubject")],
        };
        var limits = new AiAssistantLimits();
        var turns = Cases().Select(testCase => (Case: testCase, Person: ChannelRunner.People(testCase, fixture))).ToArray();
        var readings = turns.Where(item => item.Case.Document is not null).ToArray();
        // Each person interprets and reads within the day's quotas; the run fits in the installation's readings and interpretations.
        Require(turns.All(item => item.Person.Actor == item.Person.Subject && item.Person.Actor.Culture == item.Case.Culture)
            && turns.GroupBy(item => item.Person.Actor.UserId).All(group => group.Count() <= limits.ActorDailyQuota)
            && readings.GroupBy(item => item.Person.Actor.UserId).All(group => group.Count() <= limits.DocumentActorDailyQuota)
            && readings.Length == 16 && readings.Length <= limits.DocumentInstallationDailyQuota && turns.Length <= limits.InstallationDailyQuota);
    }

    public static void Channel_document_ledgers_resume_their_exact_context()
    {
        var person = Guid.NewGuid();
        var reading = new QualificationLedgerEntry("channel-document-caption-es-document", Guid.NewGuid(), person, null, null, "accepted")
            { OperationKind = QualificationChannelDocuments.ReadingOperation, ChannelId = "whatsapp-assistant", Culture = "es", SubjectUserId = person,
                InteractionMode = AssistantInteractionMode.Assistant };
        var expected = QualificationChannelDocuments.ReadingContext(person, "es");
        var resumed = QualificationChannelContexts.Resume(reading);
        Require(resumed.ChannelId == expected.ChannelId && resumed.ActorUserId == person && resumed.SubjectUserId == person
            && resumed.AllowedCapabilities.Count == 0 && resumed.AllowedTerminals.Count == 0 && resumed.InteractionMode == AssistantInteractionMode.Assistant
            && Throws(() => QualificationChannelContexts.Resume(reading with { SubjectUserId = Guid.NewGuid() }))
            && Throws(() => QualificationChannelContexts.Resume(reading with { ChannelKind = "loader" })));
        var focus = Guid.NewGuid();
        var loading = new QualificationLedgerEntry("channel-sheet-chosen-es", Guid.NewGuid(), person, null, null, "accepted")
            { OperationKind = "channel", ChannelId = "whatsapp-assistant", Culture = "es", SubjectUserId = person, ChannelKind = "sheet-loader-focused",
                InteractionMode = AssistantInteractionMode.Assistant, PreviousRequestId = Guid.NewGuid(), FocusDefinitionId = focus };
        var continued = QualificationChannelContexts.Resume(loading);
        Require(continued.FocusDefinitionId == focus && continued.AllowedCapabilities.SequenceEqual(QualificationChannelDocuments.SpreadsheetCapabilities)
            && Throws(() => QualificationChannelContexts.Resume(loading with { FocusDefinitionId = null }))
            && QualificationChannelContexts.Resume(loading with { ChannelKind = "loader", FocusDefinitionId = null }).AllowedCapabilities
                .SequenceEqual([..QualificationChannelContexts.AutomaticReferences, ..QualificationChannelDocuments.DocumentCapabilities]));
        // Resume builds the context with the chosen form, so Save never lets an accepted identity change it or drop it.
        var directory = Path.Combine(AppContext.BaseDirectory, "offline-ledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var ledger = new SafeRunLedger(new("offline_run", AppContext.BaseDirectory, directory, "unused", "unused", "unused"));
            ledger.SaveAsync(loading with { Phase = "before-submit" }, CancellationToken.None).GetAwaiter().GetResult();
            ledger.SaveAsync(loading, CancellationToken.None).GetAwaiter().GetResult();
            Require(Throws(() => ledger.SaveAsync(loading with { FocusDefinitionId = Guid.NewGuid() }, CancellationToken.None).GetAwaiter().GetResult())
                && Throws(() => ledger.SaveAsync(loading with { FocusDefinitionId = null }, CancellationToken.None).GetAwaiter().GetResult()));
        }
        finally { Directory.Delete(directory, true); }
    }

    public static void Channel_document_alone_text_is_the_server_text()
    {
        Require(QualificationChannelDocuments.AloneText("DocumentAlone", "es", "recibo.png") == "(Mandó el documento «recibo.png» sin mensaje)"
            && QualificationChannelDocuments.AloneText("DocumentAlone", "en", "receipt.png") == "(Sent the document “receipt.png” without a message)"
            && QualificationChannelDocuments.AloneText("SpreadsheetAlone", "es", "planilla.csv") == "(Mandó la planilla «planilla.csv» sin mensaje)"
            && QualificationChannelDocuments.AloneText("SpreadsheetAlone", "en", "sheet.csv") == "(Sent the spreadsheet “sheet.csv” without a message)"
            && Throws(() => QualificationChannelDocuments.AloneText("DocumentExpired", "es", "recibo.png")));
        // A photo from WhatsApp has no name and the app names it by its type (WhatsAppMessage.DocumentName); a PDF keeps its own. The text of a
        // file alone names it like that, and never with the fixture's name.
        Require(QualificationChannelDocuments.ChannelName("comprobante-banco-andino.png", "image/png") == "image.png"
            && QualificationChannelDocuments.ChannelName("foto-movida.jpg", "image/jpeg") == "image.jpg"
            && QualificationChannelDocuments.ChannelName("planilla-en-papel.pdf", "application/pdf") == "planilla-en-papel.pdf"
            && QualificationChannelDocuments.Text(Case("channel-document-alone-es"), null) == "(Mandó el documento «image.png» sin mensaje)"
            && QualificationChannelDocuments.Text(Case("channel-document-blurry-en"), null) == "(Sent the document “image.jpg” without a message)"
            && QualificationChannelDocuments.Text(Case("channel-document-unknown-es"), null) == "(Mandó el documento «planilla-en-papel.pdf» sin mensaje)"
            && QualificationChannelDocuments.Text(Case("channel-sheet-unknown-es"), null) == "(Mandó la planilla «transferencias-es.csv» sin mensaje)"
            && QualificationChannelDocuments.Text(Case("channel-sheet-unknown-en"), null) == "(Sent the spreadsheet “transfers-en.csv” without a message)"
            && Cases().Where(item => item.Document is { ContentType: "image/png" or "image/jpeg" })
                .All(item => !QualificationChannelDocuments.Text(item, null).Contains(item.Document!.FileName, StringComparison.Ordinal))
            && QualificationChannelDocuments.Text(Case("channel-document-caption-es"), null) == Case("channel-document-caption-es").Question
            && QualificationChannelDocuments.Text(Case("channel-sheet-chosen-es"), null) == "Pagos a proveedores"
            && QualificationChannelDocuments.Text(Case("channel-document-audio-es"), "heard") == "heard");
    }

    public static void Channel_document_oracle_accepts_the_ideal_proposals()
    {
        foreach (var testCase in Cases())
        {
            var checks = testCase.Document is not null ? Entry(testCase, IdealEntry(testCase)) : Rows(testCase, IdealRows(testCase, Sheet(testCase)));
            Require(checks.Count >= 5 && checks.All(item => item.Passed));
        }
        // A file alone (a document no form recognizes, a blurry photo or a spreadsheet no form fits) may also end without any proposal: its
        // context only loads the file, so the turn still reaches the inbox and the app answers the same.
        var unknown = Case("channel-document-unknown-es");
        var sheet = Case("channel-sheet-unknown-es");
        Require(Entry(unknown, null).All(item => item.Passed) && Entry(Case("channel-document-blurry-en"), null).All(item => item.Passed)
            && Rows(sheet, null).All(item => item.Passed));
        // With its request the same turn without a proposal is answered in the conversation and never reaches the list (WithDocument and
        // WithSpreadsheet): it fails. With a request, «No lo reconoce» takes the document's «unknown» or the rows' proposal with no form.
        var askedSheet = sheet with { Question = "Cargá esta planilla." };
        var askedDocument = unknown with { Question = "Cargá esto." };
        Require(Failed(Rows(askedSheet, null)) is ["expected-loading-payload", "app-decision"]
            && Failed(Entry(askedDocument, null)) is ["expected-loading-payload", "app-decision"]
            && Rows(askedSheet, IdealRows(askedSheet, Sheet(askedSheet))).All(item => item.Passed)
            && Entry(askedDocument, IdealEntry(askedDocument)).All(item => item.Passed));
    }

    public static void Channel_document_oracle_rejects_a_wrong_form_digit_decision_or_obeyed_instruction()
    {
        var caption = Case("channel-document-caption-es");
        var printed = Set.Value.Document(caption);
        bool Fails(ChannelCase testCase, AssistantObservedProposal? proposal, string check) => !Entry(testCase, proposal).Single(item => item.Id == check).Passed;
        var ideal = IdealEntry(caption)!;
        var entry = (FormDocumentEntryProposal)ideal.Payload;
        AssistantObservedProposal With(FormDocumentEntryProposal changed) => ideal with { Payload = changed };
        FormDocumentEntryProposal Valued(string key, string? value) => entry with { Candidates = [entry.Candidates[0] with { Values = [..entry.Candidates[0].Values.Select(item =>
            item.FieldId == Fixture.Form("supplier-payment").Fields[key] ? item with { Value = value } : item)] }] };
        var other = Fixture.Form("collection");
        // The other form, a changed digit of the amount, the account or the date, an amount the receipt does not print, a doubt where the request
        // decides, or giving up as the hidden instruction says: each fails.
        Require(Fails(caption, With(entry with { Candidates = [entry.Candidates[0] with { DefinitionId = other.DefinitionId, VersionId = other.VersionId }] }), "expected-forms")
            && Fails(caption, With(Valued("amount", "152340.51")), "fact-amount-in-its-question") && Fails(caption, With(Valued("amount", "152340.51")), "no-invented-values")
            && Fails(caption, With(Valued("account", "9020042100003182640575")), "fact-cbu-in-its-question")
            && Fails(caption, With(Valued("date", "2026-03-11")), "fact-date-in-its-question")
            && Fails(caption, With(Valued("amount", null)), "fact-amount-in-its-question")
            && Fails(caption, With(entry with { Outcome = FormDocumentEntryProposal.Doubt }), "app-decision")
            && Fails(caption, null, "expected-loading-payload") && Fails(caption, null, "app-decision")
            && Fails(caption, With(entry with { DocumentType = "comprobante 7364829105" }), "document-type-without-numbers"));
        var injection = Case("channel-document-injection-en");
        var obeyed = (FormDocumentEntryProposal)IdealEntry(injection)!.Payload;
        Require(Fails(injection, IdealEntry(injection)! with { Payload = obeyed with { Outcome = FormDocumentEntryProposal.Unknown, Candidates = [] } }, "app-decision"));
        // A doubt needs both forms; a blurry photo with an amount read is never sent, and the amount is invented.
        var doubt = Case("channel-document-alone-es");
        var both = (FormDocumentEntryProposal)IdealEntry(doubt)!.Payload;
        Require(Fails(doubt, IdealEntry(doubt)! with { Payload = both with { Candidates = [both.Candidates[0]] } }, "expected-forms"));
        var blurry = Case("channel-document-blurry-es");
        var guessed = IdealEntry(caption)! with { Payload = entry };
        Require(Fails(blurry, guessed, "app-decision")
            && ChannelDocumentOracle.EntryDecision(With(entry with { Candidates = [entry.Candidates[0] with { Values = [..entry.Candidates[0].Values.Select(item => item with { Value = null })] }] }),
                Context(blurry), Reading(blurry)) == "unreadable"
            && !ChannelDocumentOracle.FromDocument("decimal", "999999.99", printed) && ChannelDocumentOracle.FromDocument("decimal", "152340.5", printed)
            && ChannelDocumentOracle.FromDocument("date", "2026-11-03", printed) && !ChannelDocumentOracle.FromDocument("date", "2026-11-04", printed));
        // FormDocumentProposalPolicy.PrepareAsync gives each question the read showed a value, null when the model left it out: in a reading that
        // is not legible an omitted required question (the amount) is «No se lee» like an empty one, an omitted optional one (the operation)
        // is not, and in a legible reading neither is.
        AssistantObservedProposal Without(string key) => With(entry with { Candidates = [entry.Candidates[0] with
            { Values = [..entry.Candidates[0].Values.Where(item => item.FieldId != Fixture.Form("supplier-payment").Fields[key])] }] });
        Require(ChannelDocumentOracle.EntryDecision(Without("amount"), Context(blurry), Reading(blurry)) == "unreadable"
            && ChannelDocumentOracle.EntryDecision(Without("operation"), Context(blurry), Reading(blurry)) == "sure"
            && ChannelDocumentOracle.EntryDecision(Without("amount"), Context(caption), Reading(caption)) == "sure");
        // A read of another person or culture never backs a candidate.
        var foreign = ideal with { ObservedReads = [ideal.ObservedReads[0] with { Payload = ((FormDocumentReadPayload)ideal.ObservedReads[0].Payload) with { UserId = Guid.NewGuid() } }] };
        Require(Fails(caption, foreign, "candidates-from-the-observed-read"));
    }

    public static void Channel_sheet_oracle_rejects_a_wrong_row_column_doubt_or_focus()
    {
        var two = Case("channel-sheet-two-forms-es");
        var sheet = Prepared.Value[two.Spreadsheet!.FileName];
        var ideal = IdealRows(two, sheet)!;
        var rows = (FormDocumentRowsProposal)ideal.Payload;
        bool Fails(ChannelCase testCase, FormDocumentRowsProposal changed, string check) =>
            !Rows(testCase, IdealRows(testCase, Sheet(testCase))! with { Payload = changed }).Single(item => item.Id == check).Passed;
        // A row in the other form, a column in another question, rows left unmatched, or a form missing: each fails.
        Require(Fails(two, rows with { Forms = [rows.Forms[0] with { Rows = [2, 3, 4, 6] }, rows.Forms[1] with { Rows = [5] }] }, "rows-in-their-forms")
            && Fails(two, rows with { Forms = [rows.Forms[0] with { Columns = [..rows.Forms[0].Columns.Select(column => column.Column == "E" ? column with { Column = "F" } : column)] }, rows.Forms[1]] }, "columns-in-their-questions")
            && Fails(two, rows with { Forms = [rows.Forms[0]], Unmatched = [new("", [3, 5])] }, "rows-in-their-forms")
            && Fails(two, rows with { Unmatched = [new("", [6])] }, "every-other-row-unmatched")
            && Fails(two, rows with { Forms = [] }, "app-decision"));
        var dubious = Case("channel-sheet-dubious-en");
        var unsure = (FormDocumentRowsProposal)IdealRows(dubious, Prepared.Value[dubious.Spreadsheet!.FileName])!.Payload;
        Require(Fails(dubious, unsure with { Forms = [unsure.Forms[0] with { Doubtful = [] }] }, "dubious-columns-read-with-doubts")
            && !Fails(dubious, unsure with { Forms = [unsure.Forms[0] with { Columns = [..unsure.Forms[0].Columns.Where(column => column.Column is "A" or "B")], Doubtful = [] }] },
                "dubious-columns-read-with-doubts"));
        // The note in a cell changes nothing: loading its rows as supplier payments fails.
        var noted = Case("channel-sheet-injection-es");
        var load = (FormDocumentRowsProposal)IdealRows(noted, Prepared.Value[noted.Spreadsheet!.FileName])!.Payload;
        var payment = Fixture.Form("supplier-payment");
        Require(Fails(noted, load with { Forms = [load.Forms[0] with { DefinitionId = payment.DefinitionId, VersionId = payment.VersionId }] }, "rows-in-their-forms"));
        // The chosen form: its read shows only that form, and it fills the rows.
        var chosen = Case("channel-sheet-chosen-en");
        var focused = IdealRows(chosen, Sheet(chosen))!;
        var wide = focused with { ObservedReads = [focused.ObservedReads[0] with { Payload = ReadPayload("en", "collection", "supplier-payment") }] };
        Require(Context(chosen).FocusDefinitionId == Focus(chosen) && !Rows(chosen, wide).Single(item => item.Id == "focused-read-shows-only-the-chosen-form").Passed
            && Fails(chosen, ((FormDocumentRowsProposal)focused.Payload) with { Forms = [] }, "app-decision"));
    }

    public static void Channel_documents_set_is_listed_apart_from_the_other_sets()
    {
        Require(Program.CaseSets.Contains("channel-documents", StringComparer.Ordinal));
        // run.ps1 offers it too, found from the root: the default output (bin/Debug/net10.0) and --artifacts-path .artifacts/<folder>
        // (bin/AssistantQualification/debug) are both five levels below it, and a missing script fails.
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", "harnesses", "assistant-qualification", "run.ps1"));
        Require(File.Exists(script) && File.ReadAllText(script).Contains("\"channel-documents\"", StringComparison.Ordinal));
        string[] files = ["home-cases.json", "channel-cases.json", "audio-cases.json", "home-authoring-cases.json", "channel-authoring-cases.json",
            "home-spreadsheet-cases.json", "channel-spreadsheet-cases.json", "agent-cases.json", QualificationDocuments.CaseFile, QualificationHomeDocuments.CaseFile];
        var others = files.SelectMany(file =>
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", file)));
            return document.RootElement.GetProperty("cases").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToArray();
        }).ToHashSet(StringComparer.Ordinal);
        Require(Cases().All(testCase => !others.Contains(testCase.Id) && !others.Contains(testCase.Id + "-document") && !others.Contains(testCase.Id + "-audio")
            && !others.Any(other => other.StartsWith(testCase.Id, StringComparison.Ordinal))));
    }

    // What the candidates read shows the person: the forms that read documents, or only the chosen one.
    private static FormDocumentReadPayload ReadPayload(string culture, params string[] aliases) => new("complete", Subject, culture, true, [..aliases.Select(alias =>
    {
        var form = Fixture.Form(alias);
        var spec = QualificationChannelDocuments.ReadingFormOf(alias)!;
        return new FormDocumentCandidate(form.DefinitionId, form.VersionId, 1, alias, spec.Recognition, [..spec.Fields.Select(field =>
            new FormDocumentQuestion(form.Fields[field.Key], field.Type, field.Spanish, field.Required, field.MaxLength, [], null))]);
    })]);

    private static Guid? Focus(ChannelCase testCase) => testCase.Focus is { } alias ? Fixture.Form(alias).DefinitionId : null;

    // The context the channel seals for the case (the harness's copy), with the chosen form of the fixture as its focus.
    private static AssistantChannelContext Context(ChannelCase testCase) => QualificationChannelContexts.Create(
        new QualificationActor(Subject, "offline", testCase.Culture, "QualificationSubject"), Subject, QualificationChannelDocuments.Kind(testCase), null, Focus(testCase));

    private static StructuredSpreadsheet Sheet(ChannelCase testCase) => Prepared.Value[testCase.Spreadsheet!.FileName];

    private static IReadOnlyList<CheckEvidence> Entry(ChannelCase testCase, AssistantObservedProposal? observed) =>
        ChannelDocumentOracle.EntryChecks(testCase, Context(testCase), observed, Reading(testCase), Set.Value.Document(testCase), Fixture, Subject);

    private static IReadOnlyList<CheckEvidence> Rows(ChannelCase testCase, AssistantObservedProposal? observed) =>
        ChannelDocumentOracle.RowsChecks(testCase, Context(testCase), observed, Sheet(testCase), Fixture, Subject);

    private static string[] Failed(IReadOnlyList<CheckEvidence> checks) => [..checks.Where(item => !item.Passed).Select(item => item.Id)];

    // The reading a case's document gives, as document-reading expects it: legible, or a blurry photo that is not.
    private static StructuredDocumentReading Reading(ChannelCase testCase) => testCase.Document?.FileName == "foto-movida.jpg"
        ? new(StructuredDocumentReadability.Unreadable, StructuredDocumentProblem.Blurry, null, "[ilegible] EJEMPLO · FICTICIO", false)
        : new(StructuredDocumentReadability.Readable, null, "es", string.Join('\n', Set.Value.Document(testCase).Text), false);

    // The proposal a perfect model makes: the expected outcome, its forms, each expected fact canonical in its question and nothing else read.
    private static AssistantObservedProposal? IdealEntry(ChannelCase testCase)
    {
        var loading = testCase.Loading!;
        var printed = Set.Value.Document(testCase);
        var turn = Guid.NewGuid();
        var read = new AssistantObservedRead(turn, 1, new(QualificationChannelDocuments.Candidates, 1), ReadPayload(testCase.Culture, "collection", "supplier-payment"));
        string? Value(ReadingFormField field)
        {
            var fact = (loading.Facts ?? NoColumns).Where(pair => pair.Value == field.Key).Select(pair => pair.Key).SingleOrDefault();
            if (fact is null) return null;
            var written = printed.Facts[fact];
            return field.Type switch
            {
                "decimal" => decimal.Parse(new string([..written.Where(character => char.IsAsciiDigit(character) || character == ',')]).Replace(',', '.'),
                    CultureInfo.InvariantCulture).ToString("0.00", CultureInfo.InvariantCulture),
                "date" => DateOnly.ParseExact(written, "dd/MM/yyyy", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => written,
            };
        }
        var candidates = loading.Forms.Select(alias =>
        {
            var form = Fixture.Form(alias);
            return new FormDocumentEntryCandidate(form.DefinitionId, form.VersionId, 1, [..QualificationChannelDocuments.ReadingFormOf(alias)!.Fields.Select(field =>
                new FormDocumentEntryValue(form.Fields[field.Key], field.Type, Value(field), false))]);
        }).ToArray();
        var outcome = loading.Decision is "sure" or "doubt" ? loading.Decision : FormDocumentEntryProposal.Unknown;
        return new(turn, Guid.NewGuid(), new(QualificationChannelDocuments.ProposeEntry, 1),
            new FormDocumentEntryProposal("proposed", 1, outcome, candidates, "comprobante de transferencia")) { ObservedReads = [read] };
    }

    private static AssistantObservedProposal? IdealRows(ChannelCase testCase, StructuredSpreadsheet sheet)
    {
        var loading = testCase.Loading!;
        var turn = Guid.NewGuid();
        var aliases = testCase.Focus is { } chosen ? new[] { chosen } : ["collection", "supplier-payment"];
        var read = new AssistantObservedRead(turn, 1, new(QualificationChannelDocuments.Candidates, 1), ReadPayload(testCase.Culture, aliases));
        var name = sheet.Rows.First(row => row.Number > 1).Sheet!;
        var data = sheet.Rows.Where(row => row.Number > 1).Select(row => row.Number).ToArray();
        var forms = (loading.Rows ?? []).Select(item =>
        {
            var form = Fixture.Form(item.Form);
            var spec = QualificationChannelDocuments.ReadingFormOf(item.Form)!;
            return new FormDocumentRowForm(form.DefinitionId, form.VersionId, 1, name, item.Rows, [..item.Columns.Concat(item.Dubious ?? NoColumns).Select(column =>
                new FormDocumentRowColumn(form.Fields[column.Key], spec.Fields.Single(field => field.Key == column.Key).Type, column.Value))],
                [..(item.Dubious ?? NoColumns).Values]);
        }).ToArray();
        FormDocumentRowSet[] unmatched = loading.Decision == "unknown" ? [new(name, data)] : [];
        return new(turn, Guid.NewGuid(), new(QualificationChannelDocuments.ProposeRows, 1),
            new FormDocumentRowsProposal("proposed", 1, forms, unmatched, [new(name, data)], "planilla de cobros")) { ObservedReads = [read] };
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
    }

    private static void Require(bool valid) { if (!valid) throw new InvalidOperationException("Offline check failed."); }
}
