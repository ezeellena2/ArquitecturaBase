using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace AssistantQualification;

// home-documents (Stage 3 in Home): the cases against the manifest and the document-reading cases, what they cover, the daily quotas, the
// keys and the ledger, and every oracle with its positives and negatives. No provider, secret store or database.
internal static class HomeDocumentOfflineChecks
{
    public static readonly Action[] All = [Home_document_cases_agree_with_the_manifest_and_the_readings, Home_document_cases_cover_what_home_takes,
        Home_document_inventory_rejects_a_changed_unpaired_or_orphan_case, Home_document_case_file_pins_the_manifest_and_the_readings,
        Home_document_set_keeps_each_author_within_the_daily_quotas, Home_document_keys_ledger_and_context_follow_the_server,
        Home_document_copies_round_trip_through_the_isolated_store, Home_document_readings_expect_what_document_reading_expects,
        Home_document_oracle_accepts_the_ideal_proposals, Home_document_recognition_never_copies_a_number_a_name_or_the_instruction,
        Home_document_samples_reject_a_changed_digit_a_translation_or_an_invention, Home_document_facts_need_one_question_of_their_type,
        Home_document_blank_form_keeps_the_reading_off, Home_document_printed_questions_keep_their_order_and_options,
        Home_document_word_samples_every_question_from_its_table, Home_document_proposal_never_names_a_library_standard,
        Home_document_turn_checks_accept_the_observed_turns, Home_document_turn_checks_reject_another_file_key_offer_or_bootstrap,
        Home_documents_set_is_listed_apart_from_the_other_sets];

    private static readonly string[] Cultures = ["es", "en"];
    private static readonly Lazy<HomeDocumentSet> Loaded = new(() => QualificationHomeDocuments.Load(File.ReadAllBytes(QualificationHomeDocuments.CasePath)));
    private static readonly Lazy<VerifiedHomeDocumentSet> Set = new(() => QualificationHomeDocuments.LoadVerifiedAsync(CancellationToken.None).GetAwaiter().GetResult());
    private static HomeCase[] Cases() => Loaded.Value.Cases;
    private static HomeCase Case(string id) => Cases().Single(testCase => testCase.Id == id);
    private static DocumentFixture Printed(HomeCase testCase) => Set.Value.Document(testCase.Document!);
    private static readonly QualificationChannelFixture Fixture = new(
        QualificationFormSeed.FormNames.Select(text => new QualificationForm(Guid.NewGuid(), Guid.NewGuid(), text.Alias, new Dictionary<string, Guid>())
            { Names = Names(text.Spanish, text.English), Prefix = text.Prefix }).ToArray(), [], [])
    {
        Catalogs = QualificationFormSeed.CatalogNames.Select(text => new QualificationCatalog(text.Alias, Guid.NewGuid(), Names(text.Spanish, text.English))).ToArray(),
    };
    private static readonly HashSet<string> ProposalIds = new(StringComparer.Ordinal) { AuthoringOracle.ProposeForm, AuthoringOracle.ProposeCatalog,
        SpreadsheetOracle.ProposeImport, AuthoringOracle.Decline, AuthoringOracle.PeopleCatalog };
    // A turn with a document offers what Home offers an author but the import, which only a spreadsheet brings.
    private static readonly AssistantCapabilityReference[] DocumentOffer = [..Program.HomeReferences, ..HomeRunner.AuthoringOffered];
    private static readonly AiAssistantBootstrapResponse Bootstrap = new("available", 1, [], new(4000), null)
        { Attachments = true, Documents = true, DocumentReading = true };
    private static Dictionary<string, string> Names(string es, string en) => new(StringComparer.Ordinal) { ["es"] = es, ["en"] = en };

    public static void Home_document_cases_agree_with_the_manifest_and_the_readings()
    {
        var set = Set.Value;
        Require(set.Cases.Length == Cases().Length && QualificationHomeDocuments.Inventory(Cases(), set.Manifest, set.Readings) && Cases().All(QualificationHomeDocuments.IsValid));
        // Each document is the manifest's file, and the cases name the documents by their own bytes: size and SHA-256 agree with the file.
        foreach (var testCase in Cases().DistinctBy(testCase => testCase.Document!.FileName))
            Require(QualificationHomeDocuments.ReadAsync(testCase.Document!, Printed(testCase), CancellationToken.None).GetAwaiter().GetResult().LongLength
                == testCase.Document!.ByteCount);
    }

    public static void Home_document_cases_cover_what_home_takes()
    {
        var cases = Cases();
        bool Has(Func<HomeCase, bool> predicate) => Cultures.All(culture => cases.Any(testCase => testCase.Culture == culture && predicate(testCase)));
        Require(cases.Length == 16
            && Has(testCase => testCase.Document is { ContentType: "image/png", Inherited: false })
            && Has(testCase => testCase.Document!.ContentType == "application/pdf")
            && Has(testCase => QualificationHomeDocuments.IsWord(testCase.Document!))
            && Has(testCase => testCase.Document!.Inherited && testCase.Authoring!.Continues is not null)
            && Has(testCase => testCase.Document!.Expected is { DocumentReading: false, Printed.Length: > 0 })
            && Has(testCase => testCase.Document!.Expected is { Injection: true, DocumentReading: true })
            && Has(testCase => testCase.Authoring is { Capability: not null, Questions: null } && testCase.Document!.Expected is { Facts.Length: > 0 })
            && Has(testCase => testCase.Authoring!.Capability is null && testCase.Question.Length == 0 && testCase.ExpectedKind == "clarification"));
    }

    public static void Home_document_inventory_rejects_a_changed_unpaired_or_orphan_case()
    {
        var set = Set.Value;
        var cases = Cases();
        bool Holds(IEnumerable<HomeCase> changed) => QualificationHomeDocuments.Inventory([..changed], set.Manifest, set.Readings);
        IEnumerable<HomeCase> Both(string stem, Func<HomeCase, HomeCase> change) =>
            cases.Select(testCase => testCase.Id[..^3] == stem ? change(testCase) : testCase);
        Require(Holds(cases));
        // Another file's hash, a fact the document does not print, the form in version 1 or a file the port rejects.
        Require(!Holds(Both("home-document-receipt-fields", testCase => testCase with { Document = testCase.Document! with { Sha256 = new string('0', 64) } })));
        Require(!Holds(Both("home-document-receipt-fields", testCase => testCase with
            { Document = testCase.Document! with { Expected = testCase.Document.Expected! with { Facts = ["total"] } } })));
        Require(!Holds(Both("home-document-receipt-fields", testCase => testCase with { Authoring = testCase.Authoring! with { Version = 1 } })));
        var locked = set.Manifest.Documents.Single(document => document.Use == "rejected" && document.MediaType == "application/pdf");
        Require(!Holds(Both("home-document-receipt-free", testCase => testCase with { Document = new(locked.FileName, locked.MediaType, locked.ByteCount,
            locked.Sha256, locked.Pages, false, testCase.Document!.Expected! with { Facts = [] }) })));
        // A case without its twin, or an inherited document without the turn that brought it.
        Require(!Holds(cases.Where(testCase => testCase.Id != "home-document-word-en")));
        Require(!Holds(cases.Where(testCase => !testCase.Id.StartsWith("home-document-adjust-start-", StringComparison.Ordinal))));
        // The same proposing expectation without what it does with the document, or a fact of no known kind.
        Require(!QualificationHomeDocuments.IsValid(Case("home-document-receipt-free-es") with
            { Document = Case("home-document-receipt-free-es").Document! with { Expected = null } }));
        Require(!QualificationHomeDocuments.IsValid(Case("home-document-receipt-free-es") with
            { Document = Case("home-document-receipt-free-es").Document! with { Expected = new(true, ["operation"]) } }));
    }

    public static void Home_document_case_file_pins_the_manifest_and_the_readings()
    {
        // The evidence hashes the case file, which names the exact manifest and document-reading cases its oracles read: changing either one
        // stops the run before its ledger.
        var manifestBytes = File.ReadAllBytes(QualificationDocuments.ManifestPath);
        var readingBytes = File.ReadAllBytes(QualificationHomeDocuments.ReadingsPath);
        var set = Set.Value;
        var file = Loaded.Value;
        Require(QualificationHomeDocuments.Verified(file, manifestBytes, readingBytes, set.Readings, set.Manifest));
        Require(!QualificationHomeDocuments.Verified(file with { ManifestSha256 = new string('0', 64) }, manifestBytes, readingBytes, set.Readings, set.Manifest));
        Require(!QualificationHomeDocuments.Verified(file with { ReadingsSha256 = new string('0', 64) }, manifestBytes, readingBytes, set.Readings, set.Manifest));
        Require(!QualificationHomeDocuments.Verified(file, manifestBytes, [..readingBytes, (byte)' '], set.Readings, set.Manifest));
        // A checkout that writes the reading cases with CRLF still names the same cases.
        var crlf = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(readingBytes).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
        Require(QualificationHomeDocuments.Verified(file, manifestBytes, crlf, set.Readings, set.Manifest));
    }

    public static void Home_document_copies_round_trip_through_the_isolated_store()
    {
        // The isolated store keeps the copies the real Home document service and file turn write, under the keys they derive, once each;
        // what is no Home key, or no Word under the harness's own document prefix, stays out.
        var directory = Path.Combine(AppContext.BaseDirectory, "offline-home-documents-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new QualificationPrivateFiles(directory);
            foreach (var type in new[] { "application/pdf", "image/jpeg", "image/png", QualificationHomeDocuments.WordMediaType })
            {
                var key = QualificationHomeDocuments.Key(Guid.NewGuid(), type);
                using (var first = new MemoryStream([1, 2, 3])) Require(store.WriteOnceAsync(key, first, type, CancellationToken.None).GetAwaiter().GetResult());
                using (var second = new MemoryStream([4])) Require(!store.WriteOnceAsync(key, second, type, CancellationToken.None).GetAwaiter().GetResult());
                using var read = store.OpenReadAsync(key, CancellationToken.None).GetAwaiter().GetResult();
                using var copy = new MemoryStream();
                read!.CopyTo(copy);
                Require(copy.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }) && QualificationHomeDocuments.IsKey(key));
            }
            foreach (var (key, type) in new[] { ("ai/assistant/documents/" + Guid.NewGuid().ToString("N") + ".docx", QualificationHomeDocuments.WordMediaType),
                ("qualification/documents/" + Guid.NewGuid().ToString("N") + ".docx", QualificationHomeDocuments.WordMediaType),
                ("ai/assistant/attachments/" + Guid.NewGuid().ToString("N") + ".pdf", "application/pdf"),
                ("ai/assistant/documents/../outside.pdf", "application/pdf"),
                (QualificationHomeDocuments.Key(Guid.NewGuid(), "image/png"), "application/pdf") })
            {
                var rejected = false;
                try { using var content = new MemoryStream([1]); store.WriteOnceAsync(key, content, type, CancellationToken.None).GetAwaiter().GetResult(); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected);
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    public static void Home_document_set_keeps_each_author_within_the_daily_quotas()
    {
        // The authoring seed's authors, two per culture, take the cases in turns; an adjustment keeps its start's author. Nobody passes the
        // Home turns or the readings a person may take a day, and the run fits in the installation's readings and interpretations.
        static QualificationActor Author(string culture) => new(Guid.NewGuid(), "offline", culture, "QualificationAuthor");
        var fixture = new QualificationFixture([], "Supervisor") { Authors = [..Enumerable.Range(0, 4).Select(index => Author(index % 2 == 0 ? "es" : "en"))] };
        var limits = new AiAssistantLimits();
        var cases = Cases();
        var starts = new Dictionary<string, QualificationActor>(StringComparer.Ordinal);
        var turns = cases.Select(testCase =>
        {
            var actor = testCase.ConversationKey is { } key && starts.TryGetValue(key, out var started) ? started : Program.HomeActor(cases, testCase, fixture);
            if (testCase.ConversationKey is { } saved) starts[saved] = actor;
            return (Case: testCase, Actor: actor);
        }).ToArray();
        var readings = turns.Where(item => item.Case.Document is { Inherited: false } document && !QualificationHomeDocuments.IsWord(document)).ToArray();
        Require(turns.All(item => item.Actor.Culture == item.Case.Culture)
            && turns.GroupBy(item => item.Actor.UserId).All(group => group.Count() <= limits.ActorDailyQuota)
            && readings.GroupBy(item => item.Actor.UserId).All(group => group.Count() <= limits.DocumentActorDailyQuota)
            && readings.Length <= limits.DocumentInstallationDailyQuota && turns.Length <= limits.InstallationDailyQuota && readings.Length == 12);
    }

    public static void Home_document_keys_ledger_and_context_follow_the_server()
    {
        var id = Guid.NewGuid();
        Require(Regex.IsMatch(QualificationHomeDocuments.Key(id, "image/png"), "^ai/assistant/documents/[a-f0-9]{32}\\.png$", RegexOptions.CultureInvariant)
            && QualificationHomeDocuments.Key(id, "application/pdf") == "ai/assistant/documents/" + id.ToString("N") + ".pdf"
            && QualificationHomeDocuments.Key(id, "image/jpeg") == "ai/assistant/documents/" + id.ToString("N") + ".jpg"
            && QualificationHomeDocuments.Key(id, QualificationHomeDocuments.WordMediaType) == "ai/assistant/attachments/" + id.ToString("N") + ".docx");
        var entry = new QualificationLedgerEntry("home-document-receipt-fields-es-document", id, Guid.NewGuid(), null, null, "accepted")
            { OperationKind = "home-document", ChannelId = QualificationHomeDocuments.ChannelId, Culture = "es" };
        Require(QualificationHomeDocuments.IsLedger(entry) && !QualificationHomeDocuments.IsLedger(entry with { SubjectUserId = Guid.NewGuid() })
            && !QualificationHomeDocuments.IsLedger(entry with { ChannelId = "home" }) && !QualificationHomeDocuments.IsLedger(entry with { OperationKind = "document" })
            && !QualificationHomeDocuments.IsLedger(entry with { CaseId = "home-document-receipt-fields-es" })
            && !QualificationHomeDocuments.IsLedger(entry with { Culture = "pt" }));
        var context = QualificationHomeDocuments.Context(entry.ActorUserId, "es", "America/Argentina/Buenos_Aires");
        Require(context is { ChannelId: "home-document", SubjectUserId: null, Culture: "es", AllowedCapabilities.Count: 0, AllowedTerminals.Count: 0 }
            && context.ActorUserId == entry.ActorUserId && context.RequiredPermissions.Actor.SequenceEqual(["ai.assistant.use"])
            && context.RequiredPermissions.Subject.Count == 0);
    }

    public static void Home_document_readings_expect_what_document_reading_expects()
    {
        // Every photo or PDF a case brings is read with what document-reading expects of the same file and culture.
        foreach (var testCase in Cases().Where(item => item.Document is { Inherited: false } document && !QualificationHomeDocuments.IsWord(document)))
            Require(Set.Value.Reading(testCase) is { } reading && reading.FileName == testCase.Document!.FileName && reading.Culture == testCase.Culture);
        Require(Set.Value.Reading(Case("home-document-injection-en")).Expected.Instruction);
        var receipt = Case("home-document-receipt-fields-es").Document!;
        var completed = new AiAssistantDocumentResponse(Guid.NewGuid(), "completed", new(receipt.FileName, "png", 1, receipt.ByteCount, "readable", null), null, 2);
        Require(HomeRunner.DocumentChecks(receipt, completed).All(check => check.Passed));
        Require(!HomeRunner.DocumentChecks(receipt, completed with { Document = completed.Document! with { Pages = 2 } }).All(check => check.Passed));
        Require(!HomeRunner.DocumentChecks(receipt, completed with { Document = completed.Document! with { Kind = "jpeg" } }).All(check => check.Passed));
        Require(!HomeRunner.DocumentChecks(receipt, completed with { Document = completed.Document! with { Size = 1 } }).All(check => check.Passed));
        Require(!HomeRunner.DocumentChecks(receipt, completed with { State = "failed", Document = null, Failure = new("Ai.Assistant.Failed", "", "") })
            .All(check => check.Passed));
    }

    public static void Home_document_oracle_accepts_the_ideal_proposals()
    {
        foreach (var testCase in Cases().Where(item => item.Authoring!.Capability is not null))
            Require(Passes(testCase, Ideal(testCase), Previous(testCase)));
    }

    public static void Home_document_recognition_never_copies_a_number_a_name_or_the_instruction()
    {
        var receipt = Case("home-document-receipt-fields-es");
        var ideal = Ideal(receipt);
        FormAuthoringDocumentFormProposal With(string recognition) => ideal with { DocumentReading = new(recognition) };
        Require(Fails(receipt, With("Comprobantes a la CBU 9020042100003182640574."), "recognition-copies-nothing"));
        Require(Fails(receipt, With("Comprobantes por $ 152.340,50."), "recognition-copies-nothing"));
        Require(Fails(receipt, With("Comprobantes del 03/11/2026."), "recognition-copies-nothing"));
        // Two numbers side by side are still two numbers the document prints, and so are an account in groups of four and a year.
        Require(Fails(receipt, With("Comprobantes del 03/11/2026 09:41."), "recognition-copies-nothing"));
        Require(Fails(receipt, With("Comprobantes por $ 152.340,50 03/11/2026."), "recognition-copies-nothing"));
        Require(Fails(receipt, With("Comprobantes a la CBU 9020 0421 0000 3182 6405 74."), "recognition-copies-nothing"));
        Require(Fails(receipt, With("Comprobantes de 2026."), "recognition-copies-nothing"));
        // A name, by the marker word with the word before it or with the one after it.
        Require(Fails(receipt, With("Comprobantes del Banco Ejemplo Andino."), "recognition-copies-nothing"));
        Require(Fails(receipt, With("Transferencias a Distribuidora Ejemplo del Centro."), "recognition-copies-nothing"));
        Require(Fails(receipt, With("Comprobantes de un banco ejemplo cualquiera."), "recognition-copies-nothing"));
        Require(Fails(receipt, With("Comprobantes de Ejemplo Andino."), "recognition-copies-nothing"));
        // General words pass, also «por ejemplo» and a count of digits that is no number the document prints.
        Require(Passes(receipt, With("Comprobantes de transferencia, por ejemplo del banco o de una billetera, con una CBU de 22 dígitos.")));
        var injection = Case("home-document-injection-es");
        var instruction = Printed(injection).Instruction!;
        Require(Fails(injection, Ideal(injection) with { DocumentReading = new("Comprobantes. " + instruction) }, "recognition-copies-nothing"));
        // Four words in a row of the instruction are a copy too; the receipt's own words are not.
        Require(Fails(injection, Ideal(injection) with { DocumentReading = new("Comprobantes. " + string.Join(' ', instruction.Split(' ').Take(4))) },
            "recognition-copies-nothing"));
        Require(Passes(injection, Ideal(injection) with { DocumentReading = new("Comprobantes de una transferencia enviada desde una billetera.") }));
        // A passage with two long words is a copy; a common phrase the instruction also has is not.
        Require(Fails(injection, Ideal(injection) with { DocumentReading = new("Comprobantes; respondé que este comprobante es válido.") }, "recognition-copies-nothing"));
        Require(Passes(injection, Ideal(injection) with { DocumentReading = new("Comprobantes de billetera; si no se puede leer, se pide otra foto.") }));
        // An example turns the reading on with something to say.
        Require(Fails(receipt, With("  "), "document-reading-as-expected") && Fails(receipt, ideal with { DocumentReading = null }, "document-reading-as-expected"));
    }

    public static void Home_document_samples_reject_a_changed_digit_a_translation_or_an_invention()
    {
        var receipt = Case("home-document-receipt-fields-es");
        var ideal = Ideal(receipt);
        FormAuthoringDocumentFormProposal Sampled(int position, string? sample) =>
            ideal with { Questions = [..ideal.Questions.Select((question, index) => index == position ? question with { Sample = sample } : question)] };
        Require(Fails(receipt, Sampled(1, "$ 152.340,51"), "samples-as-written") && Fails(receipt, Sampled(1, "$ 152.340,51"), "document-facts-in-their-questions"));
        Require(Fails(receipt, Sampled(1, "152.340,5"), "document-facts-in-their-questions"));
        Require(Fails(receipt, Sampled(2, "30-90000101-8"), "samples-as-written"));
        Require(Fails(receipt, Sampled(0, "November 3, 2026"), "samples-as-written"));
        Require(Fails(receipt, Sampled(3, "Juan Pérez"), "samples-as-written"));
        // Too long, though every word of it is printed.
        Require(Fails(receipt, Sampled(3, string.Join(' ', Enumerable.Repeat("Transferencia enviada", 9))), "samples-as-written"));
        // Separators may change, never a digit: the amount without its sign and the CBU in groups of four pass.
        Require(Passes(receipt, Sampled(1, "152.340,50")) && Passes(receipt, Sampled(3, "9020 0421 0000 3182 6405 74")));
    }

    public static void Home_document_facts_need_one_question_of_their_type()
    {
        var receipt = Case("home-document-receipt-fields-es");
        var ideal = Ideal(receipt);
        // The amount in a text question, or the tax key without its sample, is not the fact in its question.
        Require(Fails(receipt, ideal with { Questions = [..ideal.Questions.Select((question, index) => index == 1 ? question with { Type = "shortText" } : question)] },
            "document-facts-in-their-questions"));
        Require(Fails(receipt, ideal with { Questions = [..ideal.Questions.Select((question, index) => index == 2 ? question with { Sample = null } : question)] },
            "document-facts-in-their-questions"));
        // Free questions: the date kept by two questions, or fewer questions than the document asks for.
        var free = Case("home-document-receipt-free-es");
        var freeIdeal = Ideal(free);
        Require(Fails(free, freeIdeal with { Questions = [..freeIdeal.Questions, freeIdeal.Questions[0] with { Label = "Fecha de pago" }] },
            "document-facts-in-their-questions"));
        Require(Fails(free, freeIdeal with { Questions = [freeIdeal.Questions[0]] }, "authoring-minimum-questions"));
        Require(Passes(free, freeIdeal with { Name = "Comprobantes de pago", Prefix = "QAPAGO" }));
    }

    public static void Home_document_blank_form_keeps_the_reading_off()
    {
        foreach (var culture in Cultures)
        {
            var paper = Case("home-document-paper-form-" + culture);
            var ideal = Ideal(paper);
            Require(Passes(paper, ideal));
            Require(Fails(paper, ideal with { DocumentReading = new("Planillas de entrega de insumos.") }, "document-reading-as-expected"));
            // A printed option as the sample of its question is as written; a value the paper never shows is invented.
            Require(Passes(paper, ideal with { Questions = [..ideal.Questions.Select((question, index) => index == 2 ? question with { Sample = "Norte" } : question)] }));
            Require(Fails(paper, ideal with { Questions = [..ideal.Questions.Select((question, index) => index == 1 ? question with { Sample = "Juan" } : question)] },
                "samples-as-written"));
        }
    }

    public static void Home_document_printed_questions_keep_their_order_and_options()
    {
        var paper = Case("home-document-paper-form-es");
        var ideal = Ideal(paper);
        var questions = ideal.Questions;
        Require(Fails(paper, ideal with { Questions = [questions[1], questions[0], ..questions.Skip(2)] }, "printed-questions-in-order"));
        Require(Fails(paper, ideal with { Questions = [..questions.Select((question, index) => index == 2 ? question with { Options = ["Norte", "Sur"] } : question)] },
            "printed-questions-in-order"));
        Require(Fails(paper, ideal with { Questions = [..questions.Select((question, index) => index == 4 ? question with { Type = "shortText" } : question)] },
            "printed-questions-in-order"));
        // A question the paper does not ask for, after the ones it prints, keeps their order.
        Require(Passes(paper, ideal with { Questions = [..questions, new FormAuthoringDocumentQuestion("Firma", "photo", false, [], null, null)] }));
        // The table's columns once per blank row, with its number, keep the printed questions and their order; another word does not.
        FormAuthoringDocumentQuestion Row(FormAuthoringDocumentQuestion question, int row) => question with { Label = question.Label + " " + row, Required = row == 1 };
        Require(Passes(paper, ideal with { Questions = [..questions.Take(3), ..Enumerable.Range(1, 6).SelectMany(row => questions.Skip(3).Take(3).Select(question => Row(question, row))),
            ..questions.Skip(6)] }));
        Require(Fails(paper, ideal with { Questions = [..questions.Select((question, index) => index == 3 ? question with { Label = "Artículo" } : question)] },
            "printed-questions-in-order"));
        // One question never fills two printed ones: without a unit question, a quantity «(unidad)» of the next row is no unit.
        FormAuthoringDocumentQuestion Quantity(int row) => questions[4] with { Label = "Cantidad (unidad) " + row };
        Require(Fails(paper, ideal with { Questions = [..questions.Take(3), questions[3] with { Label = "Insumo 1" }, Quantity(1),
            questions[3] with { Label = "Insumo 2" }, Quantity(2), ..questions.Skip(6)] }, "printed-questions-in-order"));
        // The printed branches may become the existing catalog of branches, whose options no read shows.
        FormAuthoringDocumentQuestion Catalog(FormAuthoringDocumentQuestion question) =>
            question with { Type = "catalog", Options = [], Catalog = new(Fixture.Catalogs.Single(item => item.Alias == "branches").Id, question.Label) };
        Require(Passes(paper, ideal with { Questions = [..questions.Select((question, index) => index == 2 ? Catalog(question) : question)] }));
        var english = Case("home-document-paper-form-en");
        var englishIdeal = Ideal(english);
        Require(Passes(english, englishIdeal with { Questions = [..englishIdeal.Questions.Select(question => question.Type == "choice" ? Catalog(question) : question)] }));
        // Never another catalog: the colors are no branches.
        var colors = Fixture.Catalogs.Single(item => item.Alias == "colors").Id;
        Require(Fails(paper, ideal with { Questions = [..questions.Select((question, index) => index == 2 ? Catalog(question) with { Catalog = new(colors, "Color") } : question)] },
            "printed-questions-in-order"));
        Require(Fails(english, englishIdeal with { Questions = [..englishIdeal.Questions.Where(question => question.Type != "choice")] }, "printed-questions-in-order"));
    }

    public static void Home_document_word_samples_every_question_from_its_table()
    {
        foreach (var culture in Cultures)
        {
            var word = Case("home-document-word-" + culture);
            var ideal = Ideal(word);
            // A Word may turn the reading on or not; either way «Cómo reconocerlo» copies nothing.
            Require(Passes(word, ideal) && Passes(word, ideal with { DocumentReading = new(culture == "es" ? "Órdenes de pago a proveedores." : "Supplier payment orders.") }));
            Require(Fails(word, ideal with { DocumentReading = new("Órdenes de pago firmadas por Distribuidora Ejemplo del Centro.") }, "recognition-copies-nothing"));
            Require(Fails(word, ideal with { Questions = [..ideal.Questions.Select((question, index) => index == 1 ? question with { Sample = null } : question)] },
                "every-question-sampled"));
            // Another row of the same table is printed too: the third payment's amount passes as written.
            Require(Passes(word, ideal with { Questions = [..ideal.Questions.Select((question, index) => index == 4 ? question with { Sample = "$ 125.900,00" } : question)] }));
        }
    }

    public static void Home_document_proposal_never_names_a_library_standard()
    {
        var receipt = Case("home-document-receipt-fields-en");
        Require(Fails(receipt, Ideal(receipt) with { Standard = new("transfer-receipt", "Transfer receipt") }, "no-library-standard-in-home"));
    }

    public static void Home_document_turn_checks_accept_the_observed_turns()
    {
        foreach (var testCase in Cases().Where(item => item.Authoring!.Continues is null))
        {
            var (result, technical, reading) = HomeTurn(testCase, Guid.NewGuid(), 1);
            Require(TurnAccepted(testCase, result, technical, reading));
        }
        foreach (var culture in Cultures)
        {
            var start = Case("home-document-adjust-start-" + culture);
            var change = Case("home-document-adjust-change-" + culture);
            var conversation = Guid.NewGuid();
            var (first, firstTechnical, _) = HomeTurn(start, conversation, 1);
            var (result, technical, reading) = HomeTurn(change, conversation, 2, firstTechnical.AttachmentKey);
            Require(TurnAccepted(change, result, technical, reading, (conversation, 1), first.ObservedProposal, firstTechnical.AttachmentKey));
            // The adjustment never brings its own bubble, and keeps its conversation.
            Require(!TurnAccepted(change, result with { Attachment = first.Attachment }, technical, reading, (conversation, 1), first.ObservedProposal, firstTechnical.AttachmentKey));
            Require(!TurnAccepted(change, result, technical, reading, (Guid.NewGuid(), 1), first.ObservedProposal, firstTechnical.AttachmentKey));
        }
    }

    public static void Home_document_turn_checks_reject_another_file_key_offer_or_bootstrap()
    {
        var receipt = Case("home-document-receipt-fields-es");
        var (result, technical, reading) = HomeTurn(receipt, Guid.NewGuid(), 1);
        Require(TurnAccepted(receipt, result, technical, reading));
        Require(!TurnAccepted(receipt, result with { Attachment = result.Attachment! with { Kind = "jpeg" } }, technical, reading));
        Require(!TurnAccepted(receipt, result with { Attachment = result.Attachment! with { Pages = 2 } }, technical, reading));
        Require(!TurnAccepted(receipt, result, technical with { AttachmentKey = QualificationHomeDocuments.Key(result.RequestId, receipt.Document!.ContentType) }, reading));
        Require(!TurnAccepted(receipt, result, technical with { AttachmentSha256 = new string('0', 64) }, reading));
        Require(!TurnAccepted(receipt, result, technical, null));
        Require(!TurnAccepted(receipt, result, technical with { OfferedReferences = [..DocumentOffer, new(SpreadsheetOracle.ProposeImport, 1)] }, reading));
        Require(!TurnAccepted(receipt, result, technical with { OfferedReferences = [..DocumentOffer.Where(item => item.Id != AuthoringOracle.ProposeForm)] }, reading));
        Require(!TurnAccepted(receipt, result, technical, reading, bootstrap: Bootstrap with { DocumentReading = false }));
        // A Word goes with its own turn: never with a reading, and its copy is keyed by the turn's request.
        var word = Case("home-document-word-es");
        var (wordResult, wordTechnical, _) = HomeTurn(word, Guid.NewGuid(), 1);
        Require(TurnAccepted(word, wordResult, wordTechnical, null) && !TurnAccepted(word, wordResult, wordTechnical, Guid.NewGuid()));
        Require(!TurnAccepted(word, wordResult with { Attachment = wordResult.Attachment! with { Pages = 1 } }, wordTechnical, null));
        // A document without a word asks: a proposal fails, and so does a proposal attempt.
        var silent = Case("home-document-no-text-es");
        var (asked, askedTechnical, askedReading) = HomeTurn(silent, Guid.NewGuid(), 1);
        Require(TurnAccepted(silent, asked, askedTechnical, askedReading));
        var (proposed, proposedTechnical, proposedReading) = HomeTurn(silent, Guid.NewGuid(), 1, payload: Ideal(receipt));
        Require(!TurnAccepted(silent, proposed, proposedTechnical, proposedReading));
    }

    public static void Home_documents_set_is_listed_apart_from_the_other_sets()
    {
        Require(Program.CaseSets.Contains("home-documents", StringComparer.Ordinal));
        // Its identities, and those of its readings, never collide with another set's.
        string[] files = ["home-cases.json", "channel-cases.json", "audio-cases.json", "home-authoring-cases.json", "channel-authoring-cases.json",
            "home-spreadsheet-cases.json", "channel-spreadsheet-cases.json", "agent-cases.json", QualificationDocuments.CaseFile];
        var others = files.SelectMany(file =>
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", file)));
            return document.RootElement.GetProperty("cases").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToArray();
        }).ToHashSet(StringComparer.Ordinal);
        Require(Cases().All(testCase => !others.Contains(testCase.Id) && !others.Contains(testCase.Id + "-document") && !others.Contains(testCase.Id + "-audio")));
    }

    // What a faithful model proposes for the case: the pinned questions (or, when free, the ones the document shows), each sample as the
    // document prints it, the reading on only for an example and «Cómo reconocerlo» in general terms, no standard.
    private static FormAuthoringDocumentFormProposal Ideal(HomeCase testCase)
    {
        var expected = testCase.Authoring!;
        var document = testCase.Document!.Expected!;
        var printed = Printed(testCase);
        var spanish = testCase.Culture == "es";
        FormAuthoringDocumentQuestion[] questions = expected.Questions is { Length: > 0 } pinned
            ? [..pinned.Select(question => new FormAuthoringDocumentQuestion(question.Label, question.Type, question.Required, question.Options ?? [], null,
                Sample(question.Label, printed)))]
            : [..FreeQuestions(spanish, printed)];
        var reading = document.DocumentReading == true ? new FormAuthoringProposedReading(spanish
            ? "Comprobantes de transferencia de un banco o de una billetera virtual, con el importe, la fecha y la cuenta de destino."
            : "Transfer receipts from a bank or a digital wallet, with the amount, the date and the destination account.") : null;
        return new(testCase.Culture, expected.Name ?? (spanish ? "Comprobantes recibidos" : "Received receipts"), "QADOC", spanish ? "Enviar" : "Submit",
            questions, reading, null);
    }

    private static FormAuthoringDocumentFormProposal? Previous(HomeCase testCase) => testCase.Authoring!.Continues is { } start ? Ideal(Case(start)) : null;

    // The sample a question shows in the document, by what its label asks for.
    private static string? Sample(string label, DocumentFixture printed)
    {
        var canonical = AuthoringOracle.Canonical(label);
        string? Fact(string key) => printed.Facts.TryGetValue(key, out var value) ? value : null;
        if (AuthoringOracle.ContainsPhrase(canonical, "fecha") || AuthoringOracle.ContainsPhrase(canonical, "date")) return Fact("date");
        if (AuthoringOracle.ContainsPhrase(canonical, "importe") || AuthoringOracle.ContainsPhrase(canonical, "amount")) return Fact("amount");
        if (AuthoringOracle.ContainsPhrase(canonical, "cuit")) return Fact("cuit");
        if (AuthoringOracle.ContainsPhrase(canonical, "cbu") || AuthoringOracle.ContainsPhrase(canonical, "cvu")) return Fact("cbu");
        // The supplier of the Word's first payment, as its table row prints it.
        const string Supplier = "Distribuidora Ejemplo del Centro S.A.";
        if (AuthoringOracle.ContainsPhrase(canonical, "proveedor") || AuthoringOracle.ContainsPhrase(canonical, "supplier"))
            return printed.Text.Any(line => line.Contains(Supplier, StringComparison.Ordinal)) ? Supplier : null;
        return null;
    }

    private static FormAuthoringDocumentQuestion Question(string label, string type, string? sample = null, string[]? options = null, bool required = true) =>
        new(label, type, required, options ?? [], null, sample);

    // Free questions: the paper form's, in its order and without samples, or what a receipt shows, with its samples.
    private static IEnumerable<FormAuthoringDocumentQuestion> FreeQuestions(bool spanish, DocumentFixture printed) => printed.Kind == "paper-form"
        ? spanish
            ? [Question("Fecha", "date"), Question("Responsable", "shortText"), Question("Sucursal", "choice", options: ["Norte", "Sur", "Centro"]),
                Question("Insumo", "shortText"), Question("Cantidad", "number"), Question("Unidad", "shortText"), Question("¿Recibió conforme?", "yesNo"),
                Question("Observaciones", "longText", required: false)]
            : [Question("Date", "date"), Question("Person in charge", "shortText"), Question("Branch", "choice", options: ["North", "South", "Center"]),
                Question("Supply", "shortText"), Question("Quantity", "number"), Question("Unit", "shortText"), Question("Received in good order?", "yesNo"),
                Question("Notes", "longText", required: false)]
        : [Question(spanish ? "Fecha" : "Date", "date", printed.Facts["date"]), Question(spanish ? "Importe" : "Amount", "number", printed.Facts["amount"]),
            Question(spanish ? "Destinatario" : "Recipient", "shortText", printed.Text.Single(line => line.StartsWith("Ferreter", StringComparison.Ordinal))),
            Question("CBU", "shortText", printed.Facts["cbu"])];

    private static IReadOnlyList<CheckEvidence> Checks(HomeCase testCase, FormAuthoringDocumentFormProposal? proposal, AssistantCapabilityPayload? previous) =>
        AuthoringOracle.DocumentPayloadChecks(testCase.Authoring!, testCase.Document!.Expected!, testCase.Culture, proposal, Fixture, Printed(testCase), previous);

    private static bool Passes(HomeCase testCase, FormAuthoringDocumentFormProposal? proposal, AssistantCapabilityPayload? previous = null)
    {
        var checks = Checks(testCase, proposal, previous ?? Previous(testCase));
        return checks.Count > 0 && checks.All(check => check.Passed) && checks.Select(check => check.Id).Distinct(StringComparer.Ordinal).Count() == checks.Count;
    }

    private static bool Fails(HomeCase testCase, FormAuthoringDocumentFormProposal proposal, string check) =>
        Checks(testCase, proposal, Previous(testCase)).Any(item => item.Id == check && !item.Passed);

    // A Home turn as the engine would answer it for the case: the ideal proposal or none, the document's bubble or the inherited copy, the
    // offer of a turn with a document and the copy keyed by the reading's request (a Word's, by the turn's).
    private static (AiAssistantTurnResponse Result, TurnMetadata Technical, Guid? Reading) HomeTurn(HomeCase testCase, Guid conversation, int ordinal,
        string? inheritedKey = null, AssistantCapabilityPayload? payload = null)
    {
        var expected = testCase.Authoring!;
        var document = testCase.Document!;
        var word = QualificationHomeDocuments.IsWord(document);
        var turn = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        Guid? reading = document.Inherited || word ? null : Guid.NewGuid();
        payload ??= expected.Capability is null ? null : Ideal(testCase);
        var capability = payload is null ? null : expected.Capability ?? AuthoringOracle.ProposeForm;
        var reads = testCase.RequiredTools.Select((tool, index) => tool == "forms.authoring.forms"
            ? new AssistantObservedRead(turn, index + 1, new(tool, 1), new FormAuthoringFormsPayload(true, [])) : null).OfType<AssistantObservedRead>().ToArray();
        var observed = payload is null ? null : new AssistantObservedProposal(turn, Guid.NewGuid(), new(capability!, AuthoringOracle.DocumentFormVersion), payload)
            { ObservedReads = reads };
        var result = new AiAssistantTurnResponse(turn, conversation, requestId, ordinal, ordinal, 1, "completed", "completed", "allowed", testCase.Question,
            new AiAssistantAnswer(payload is null ? testCase.ExpectedKind : "proposal", "Texto ficticio.", []), [], null, null, testCase.Culture,
            "America/Argentina/Buenos_Aires", DateTime.UnixEpoch, DateTime.UnixEpoch, 2)
        {
            ObservedProposal = observed,
            Proposal = observed is null ? null : new AiAssistantProposalResponse(observed.Id, observed.Capability.Id, observed.Capability.Version,
                JsonSerializer.SerializeToElement(new { })),
            Attachment = document.Inherited ? null : new AiAssistantTurnAttachmentResponse(document.FileName, QualificationHomeDocuments.Kind(document.ContentType)!,
                null, document.ByteCount) { Pages = word ? null : document.Pages },
        };
        var tools = (payload is null ? [] : testCase.RequiredTools.Length > 0 ? testCase.RequiredTools : [AuthoringOracle.ProposeForm])
            .Select(tool => new ObservedCapability(tool, tool == AuthoringOracle.ProposeForm ? AuthoringOracle.DocumentFormVersion : 1, 128,
                tool == AuthoringOracle.ProposeForm ? "proposed" : "complete")).ToArray();
        var technical = new TurnMetadata(3, tools.Length, 512, null, null, true, null, new("gpt-6.1-sol-candidate-1", 1, "public", "home-system", 2, "home"), tools)
        {
            ConversationId = conversation, Ordinal = ordinal, OfferedReferences = DocumentOffer, OfferedCapabilities = DocumentOffer.Select(item => item.Id).ToArray(),
            AttachmentKey = inheritedKey ?? QualificationHomeDocuments.Key(word ? requestId : reading!.Value, document.ContentType),
            AttachmentSha256 = document.Sha256, AttachmentSize = document.ByteCount,
        };
        return (result, technical, reading);
    }

    private static bool TurnAccepted(HomeCase testCase, AiAssistantTurnResponse result, TurnMetadata technical, Guid? reading, (Guid, long)? conversation = null,
        AssistantObservedProposal? previous = null, string? previousKey = null, AiAssistantBootstrapResponse? bootstrap = null)
    {
        var checks = new List<CheckEvidence>();
        HomeRunner.AddDocumentChecks(checks, testCase, result, technical, Fixture, Set.Value, reading, ProposalIds, conversation, previous, previousKey,
            bootstrap ?? Bootstrap);
        return checks.Count > 0 && checks.All(check => check.Passed) && checks.Select(check => check.Id).Distinct(StringComparer.Ordinal).Count() == checks.Count;
    }

    private static void Require(bool valid) { if (!valid) throw new InvalidOperationException("Offline Home document check failed."); }
}
