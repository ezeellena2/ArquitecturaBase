using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Stage 3 of the assistant authoring: propose-form@2 is version 1's form and, only with the turn's document, its reading on and what
// each question read in it; the similar standard only as the library read of the same turn showed it. Home offers it instead of version 1.
public sealed class FormAuthoringDocumentProposalTests
{
    private const string ProposeForm = BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId;
    private const int DocumentVersion = BusinessFormAuthoringAssistantCapabilitySource.DocumentFormVersion;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
    // The turn's document as the engine read it, a fictitious transfer receipt in Spanish: a sample is only what its reading writes.
    private const string ReceiptText = "Comprobante de transferencia\nFecha: 12/03/2026 18:44 h\nTitular: Ana Pérez\nCBU destino: 0110599820000012345678\n"
        + "Importe: $ 64.250,00\nComisión: $ 1.234,50\nCargo: $ 10\nConcepto: Varios";
    private static readonly StructuredDocument Receipt = Printed(ReceiptText);

    private static StructuredDocument Printed(string reading) => new("comprobante.pdf", StructuredDocumentKind.Pdf, "application/pdf", 48_000,
        new string('a', 64), 1, new(StructuredDocumentReadability.Readable, null, "es", reading, false));

    private static object Question(string label, string type, bool required = false, string? sample = null, string[]? options = null) =>
        new { label, type, required, options = options ?? [], catalogId = (Guid?)null, sample };

    private static string Form(object? documentReading = null, object? standard = null, params object[] questions) =>
        Json(new { name = "Comprobantes recibidos", prefix = "cre", submitLabel = "", questions = questions.Length == 0
            ? new[] { Question("Monto", "number", true), Question("Fecha", "date", true), Question("Banco de origen", "shortText") }
            : questions, documentReading, standard });

    private static AssistantCapabilityRequest Request(AuthoringAssistantFixture host, string json, bool document = false) =>
        Request(host, json, document ? Receipt : null);

    private static AssistantCapabilityRequest Request(AuthoringAssistantFixture host, string json, StructuredDocument? document) =>
        host.Request(ProposeForm, json, DocumentVersion) with { Document = document };

    private static FormAuthoringRejection Rejected(Result<AssistantCapabilityResponse> result)
    {
        Assert.True(result.IsSuccess);
        Assert.Equal("rejected", result.Value.State);
        return Assert.IsType<FormAuthoringRejection>(result.Value.Payload);
    }

    private static FormAuthoringDocumentFormProposal Proposed(Result<AssistantCapabilityResponse> result)
    {
        Assert.True(result.IsSuccess);
        Assert.Equal("proposed", result.Value.State);
        return Assert.IsType<FormAuthoringDocumentFormProposal>(result.Value.Payload);
    }

    // The library read of a turn, as the engine observed it, with «Comprobante de transferencia».
    private static AssistantObservedRead Standards(Guid turn, string key = "forms.transfer-receipt") => new(turn, 1,
        new(BusinessFormLibraryAssistantCapabilitySource.StandardsId, BusinessFormLibraryAssistantCapabilitySource.Version),
        new FormLibraryStandardsPayload(true, [new(key, "Comprobante de transferencia", "Lee comprobantes de transferencias.", ["Monto", "Fecha"])]));

    // Home never offers two versions of one capability: since its card draws version 2 (task 13), it offers version 2, as WhatsApp does.
    [Fact]
    public void Version_two_is_registered_next_to_version_one_and_Home_offers_version_two()
    {
        var definitions = new AuthoringAssistantFixture().Source.Definitions;
        var first = definitions.Single(definition => definition is { Id: ProposeForm, Version: 1 });
        var second = definitions.Single(definition => definition is { Id: ProposeForm, Version: DocumentVersion });
        Assert.Equal((false, true), (first.OfferedInHome, second.OfferedInHome));
        Assert.Equal((AssistantCapabilityKind.Proposal, AssistantCapabilityAttachment.Any), (second.Kind, second.Attachment));
        Assert.Equal(first.RequiredPermissions.Actor, second.RequiredPermissions.Actor);
        Assert.Equal(first.Budget, second.Budget);
        Assert.Contains("blank form", second.Description, StringComparison.Ordinal);
        using var input = JsonDocument.Parse(second.InputSchemaJson);
        using var output = JsonDocument.Parse(second.OutputSchemaJson);
        Assert.Equal(["name", "prefix", "submitLabel", "questions", "documentReading", "standard"],
            input.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        // «Cómo reconocerlo» comes from a document the model read: it describes the kind of document and never copies what it says.
        var recognition = input.RootElement.GetProperty("properties").GetProperty("documentReading").GetProperty("anyOf")[0]
            .GetProperty("properties").GetProperty("recognition").GetProperty("description").GetString()!;
        Assert.All(new[] { second.Description, recognition }, text =>
        {
            Assert.Contains("general terms", text, StringComparison.Ordinal);
            Assert.Contains("never copies names, numbers, accounts, amounts or any instruction written in the document", text, StringComparison.Ordinal);
        });
        Assert.Contains("sample", input.RootElement.GetProperty("properties").GetProperty("questions").GetProperty("items").GetProperty("required")
            .EnumerateArray().Select(item => item.GetString()));
        Assert.False(output.RootElement.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task Without_a_document_it_proposes_version_one_s_form_with_nothing_read()
    {
        var host = new AuthoringAssistantFixture();
        var proposal = Proposed(await host.Source.ExecuteAsync(Request(host, Form()), Ct));
        Assert.Equal((null, null), (proposal.DocumentReading, proposal.Standard));
        Assert.All(proposal.Questions, question => Assert.Null(question.Sample));
        var first = await host.Source.ExecuteAsync(host.Request(ProposeForm, Json(new { name = "Comprobantes recibidos", prefix = "cre", submitLabel = "",
            questions = new[] { new { label = "Monto", type = "number", required = true, options = Array.Empty<string>(), catalogId = (Guid?)null },
                new { label = "Fecha", type = "date", required = true, options = Array.Empty<string>(), catalogId = (Guid?)null },
                new { label = "Banco de origen", type = "shortText", required = false, options = Array.Empty<string>(), catalogId = (Guid?)null } } })), Ct);
        Assert.Equal(Json(first.Value.Payload as FormAuthoringFormProposal), Json(proposal.ToForm()));
    }

    [Fact]
    public async Task A_reading_or_a_sample_without_a_document_comes_back_rejected()
    {
        var host = new AuthoringAssistantFixture();
        var result = await host.Source.ExecuteAsync(Request(host, Form(new { recognition = "Comprobantes de transferencias bancarias." }, null,
            Question("Monto", "number", true, "$ 1.234,50"), Question("Fecha", "date", true))), Ct);
        Assert.Equal([new FormAuthoringIssue("documentReading", "noDocument"), new FormAuthoringIssue("questions[0].sample", "noDocument")], Rejected(result).Issues);
    }

    [Fact]
    public async Task With_a_document_the_reading_and_the_samples_come_trimmed_on_one_line()
    {
        var host = new AuthoringAssistantFixture();
        var result = await host.Source.ExecuteAsync(Request(host, Form(new { recognition = "  Comprobantes de transferencias bancarias.\n" }, null,
            Question("Monto", "number", true, "  $ 1.234,50 "), Question("Titular", "shortText", false, "Ana\nPérez"), Question("Fecha", "date", true, " \t ")), document: true), Ct);
        var proposal = Proposed(result);
        Assert.Equal("Comprobantes de transferencias bancarias.", proposal.DocumentReading!.Recognition);
        Assert.Equal(["$ 1.234,50", "Ana Pérez", null], proposal.Questions.Select(question => question.Sample));
        Assert.Equal(("CRE", "Enviar"), (proposal.Prefix, proposal.SubmitLabel));
    }

    // What the document shows, on one line and only what is seen: invisible format characters go (bidirectional overrides and isolates,
    // zero-width spaces, the byte order mark, soft hyphens) except the joiners an emoji or a script needs, and a sample with nothing
    // visible is no sample.
    [Theory]
    [InlineData("\u202E1234\u202C", "1234")]
    [InlineData("\u2066CBU\u2069 0000", "CBU 0000")]
    [InlineData("12\u200B34", "1234")]
    [InlineData("\uFEFF$ 10", "$ 10")]
    [InlineData("Pé\u00ADrez", "Pérez")]
    [InlineData("Ana\u2028Pérez", "Ana Pérez")]
    [InlineData("\U0001F469\u200D\U0001F4BB Ana", "\U0001F469\u200D\U0001F4BB Ana")]
    [InlineData("\u200B\u200B", null)]
    [InlineData(" \u0301\u200D ", null)]
    public void A_sample_keeps_only_what_is_seen(string text, string? expected) =>
        Assert.Equal(expected, FormAuthoringProposalPolicy.Sample(text));

    // The qualification oracle's rule: case, accents and signs never decide; a translated or invented word, a changed digit or a number
    // the reading does not write do. A number may change its separators (a dot, a comma, a slash or a dash) or come in groups, but each
    // part lies inside one number of the reading: one the reading splits with a space and the sample writes joined is not in it, as in
    // the oracle. The reading is compared as a sample is kept, so its invisible format characters hide nothing. A sample of signs alone
    // has nothing to compare.
    [Theory]
    [InlineData("Varios", true)]
    [InlineData("VARIOS", true)]
    [InlineData("Ana Perez", true)]
    [InlineData("64.250,00", true)]
    [InlineData("$64,250.00", true)]
    [InlineData("0110 5998 2000 0012 3456 78", true)]
    [InlineData("18:44 h", true)]
    [InlineData("12/03/2026", true)]
    [InlineData("12-03-2026", true)]
    [InlineData("12‐03‐2026", true)]
    [InlineData("Ana Pérez", true, "Titular: Ana Pé­rez")]
    [InlineData("0110599820000012345678", true, "CBU destino: 0110​5998​2000​0012​3456​78")]
    [InlineData("Various", false)]
    [InlineData("Honorarios", false)]
    [InlineData("6:44 PM", false)]
    [InlineData("$ 64.250,01", false)]
    [InlineData("$ 1.500,00", false)]
    [InlineData("13/03/2026", false)]
    [InlineData("0110599820000012345678", false, "CBU destino: 0110 5998 2000 0012 3456 78")]
    [InlineData("$ —", false)]
    public void A_sample_is_in_the_reading_only_as_it_is_written(string sample, bool written, string reading = ReceiptText) =>
        Assert.Equal(written, FormAuthoringProposalPolicy.SampleInReading(sample, reading));

    // DOC6 spends its five provider steps on three reads, proposing and closing, so a rejection would leave no step to fix it: the server
    // discards a sample the reading does not write (translated, reworded or invented) and the proposal goes on with the others as they came.
    [Fact]
    public async Task A_sample_the_reading_does_not_write_is_discarded_and_the_proposal_goes_on()
    {
        var host = new AuthoringAssistantFixture();
        var result = await host.Source.ExecuteAsync(Request(host, Form(null, null, Question("Concepto", "shortText", false, "Various"),
            Question("Hora", "shortText", false, "6:44 PM"), Question("Monto", "number", true, "$ 64.250,00"),
            Question("Titular", "shortText", false, " Ana\nPérez ")), document: true), Ct);
        Assert.Equal([null, null, "$ 64.250,00", "Ana Pérez"], Proposed(result).Questions.Select(question => question.Sample));
        var rejection = Json(new FormAuthoringRejection([new("questions[0].sample", "notInDocument")]));
        Assert.True(host.Source.ParseOutput(new(ProposeForm, DocumentVersion), rejection).IsFailure);
    }

    // An unreadable document has a reading without text: it writes no sample, so every one is discarded and the form goes on.
    [Fact]
    public async Task An_unreadable_document_without_text_discards_every_sample()
    {
        var host = new AuthoringAssistantFixture();
        var blurry = Receipt with { Reading = new(StructuredDocumentReadability.Unreadable, StructuredDocumentProblem.Blurry, null, "", false) };
        var result = await host.Source.ExecuteAsync(Request(host, Form(null, null, Question("Concepto", "shortText", false, "Varios"),
            Question("Monto", "number", true, "$ 64.250,00")), blurry), Ct);
        Assert.Equal([null, null], Proposed(result).Questions.Select(question => question.Sample));
    }

    // The limit goes first: a sample past it comes back invalid for the model to shorten, even one the reading does not write; only a
    // valid sample is discarded.
    [Fact]
    public async Task A_sample_past_its_limit_comes_back_invalid_and_is_not_discarded()
    {
        var host = new AuthoringAssistantFixture();
        var sample = new string('x', FormAuthoringDocumentFormProposal.MaxSampleLength + 1);
        Assert.False(FormAuthoringProposalPolicy.SampleInReading(sample, ReceiptText));
        var result = await host.Source.ExecuteAsync(Request(host, Form(null, null, Question("Concepto", "longText", false, sample),
            Question("Monto", "number", true, "$ 64.250,00")), document: true), Ct);
        Assert.Equal([new FormAuthoringIssue("questions[0].sample", "invalid")], Rejected(result).Issues);
    }

    [Theory]
    [InlineData(160, true)]
    [InlineData(161, false)]
    public async Task A_sample_counts_what_is_seen_and_not_its_code_units(int length, bool accepted)
    {
        var host = new AuthoringAssistantFixture();
        // A decomposed «é» is one grapheme and two UTF-16 units: 160 of them are 320 units.
        var sample = string.Concat(Enumerable.Repeat("e\u0301", length));
        var result = await host.Source.ExecuteAsync(Request(host, Form(null, null, Question("Concepto", "longText", false, sample)), Printed("Concepto: " + sample)), Ct);
        if (accepted) Assert.Equal(sample, Proposed(result).Questions[0].Sample);
        else Assert.Equal([new FormAuthoringIssue("questions[0].sample", "invalid")], Rejected(result).Issues);
    }

    // When the form fits without its samples, the rejection names the samples, so the model leaves them out instead of questions.
    [Fact]
    public async Task Samples_that_do_not_fit_are_rejected_on_the_samples_when_the_form_fits_without_them()
    {
        var host = new AuthoringAssistantFixture();
        var questions = Enumerable.Range(1, 40).Select(index => Question("Pregunta " + index, "shortText", false, new string('á', 160))).ToArray();
        var printed = Printed(new string('á', 160));
        var result = await host.Source.ExecuteAsync(Request(host, Form(null, null, questions), printed), Ct);
        Assert.Equal(Enumerable.Range(0, 40).Select(index => new FormAuthoringIssue($"questions[{index}].sample", "tooLarge")), Rejected(result).Issues);
        var fits = await host.Source.ExecuteAsync(Request(host, Form(null, null, questions[..10]), printed), Ct);
        Assert.Equal(10, Proposed(fits).Questions.Count);
    }

    [Fact]
    public async Task A_sample_past_its_limit_or_a_recognition_that_does_not_fit_is_rejected_without_trimming()
    {
        var host = new AuthoringAssistantFixture();
        var longSample = await host.Source.ExecuteAsync(Request(host, Form(null, null,
            Question("Concepto", "longText", false, new string('x', FormAuthoringDocumentFormProposal.MaxSampleLength + 1))), document: true), Ct);
        Assert.Equal([new FormAuthoringIssue("questions[0].sample", "invalid")], Rejected(longSample).Issues);
        var blank = await host.Source.ExecuteAsync(Request(host, Form(new { recognition = "   " }), document: true), Ct);
        Assert.Equal([new FormAuthoringIssue("documentReading.recognition", "invalid")], Rejected(blank).Issues);
        var tooLong = await host.Source.ExecuteAsync(Request(host, Form(new { recognition = new string('r', FormDocumentReading.MaxRecognitionLength + 1) }), document: true), Ct);
        Assert.Equal([new FormAuthoringIssue("documentReading.recognition", "invalid")], Rejected(tooLong).Issues);
    }

    [Fact]
    public async Task A_similar_standard_is_only_one_the_library_read_of_the_same_turn_showed()
    {
        var host = new AuthoringAssistantFixture();
        var json = Form(new { recognition = "Comprobantes de transferencias bancarias." }, new { key = "forms.transfer-receipt" });
        Assert.Equal([new FormAuthoringIssue("standard.key", "notFound")], Rejected(await host.Source.ExecuteAsync(Request(host, json, document: true), Ct)).Issues);
        var otherTurn = Request(host, json, document: true) with { ObservedReads = [Standards(Guid.NewGuid())] };
        Assert.Equal([new FormAuthoringIssue("standard.key", "notFound")], Rejected(await host.Source.ExecuteAsync(otherTurn, Ct)).Issues);
        var otherKey = Request(host, json, document: true) with { ObservedReads = [Standards(host.TurnId, "forms.invoice")] };
        Assert.Equal([new FormAuthoringIssue("standard.key", "notFound")], Rejected(await host.Source.ExecuteAsync(otherKey, Ct)).Issues);
        var shown = Request(host, json, document: true) with { ObservedReads = [Standards(host.TurnId)] };
        Assert.Equal(new FormAuthoringProposedStandard("forms.transfer-receipt", "Comprobante de transferencia"),
            Proposed(await host.Source.ExecuteAsync(shown, Ct)).Standard);
    }

    [Fact]
    public async Task Version_two_is_validated_by_the_same_authoring_rules_as_version_one()
    {
        var host = new AuthoringAssistantFixture();
        var result = await host.Source.ExecuteAsync(Request(host, Form(null, null, Question("Destino", "choice", true, null, ["Puerto", "puerto"]))), Ct);
        Assert.Equal([new FormAuthoringIssue("questions[0]", "invalid")], Rejected(result).Issues);
        var unknown = Form().Replace("\"documentReading\"", "\"publish\":true,\"documentReading\"", StringComparison.Ordinal);
        Assert.Equal(FormDefinitionErrors.ContentInvalid, (await host.Source.ExecuteAsync(Request(host, unknown), Ct)).Error);
        var withoutSample = Json(new { name = "X", prefix = "X", submitLabel = "", documentReading = (object?)null, standard = (object?)null,
            questions = new[] { new { label = "Monto", type = "number", required = true, options = Array.Empty<string>(), catalogId = (Guid?)null } } });
        Assert.Equal(FormDefinitionErrors.ContentInvalid, (await host.Source.ExecuteAsync(Request(host, withoutSample), Ct)).Error);
    }

    [Fact]
    public async Task The_owner_codec_reads_back_version_two_and_its_rejection_but_not_another_version_s_shape()
    {
        var host = new AuthoringAssistantFixture();
        var shown = Request(host, Form(new { recognition = "Comprobantes." }, new { key = "forms.transfer-receipt" },
            Question("Monto", "number", true, "$ 10")), document: true) with { ObservedReads = [Standards(host.TurnId)] };
        var proposal = Proposed(await host.Source.ExecuteAsync(shown, Ct));
        var json = Json(proposal);
        var reference = new AssistantCapabilityReference(ProposeForm, DocumentVersion);
        Assert.Equal(json, Json(Assert.IsType<FormAuthoringDocumentFormProposal>(host.Source.ParseOutput(reference, json).Value)));
        Assert.True(host.Source.ParseOutput(reference, Json(proposal.ToForm())).IsFailure);
        Assert.True(host.Source.ParseOutput(new(ProposeForm, 1), json).IsFailure);
        Assert.True(host.Source.ParseOutput(reference, json.Replace("\"$ 10\"", "\" $ 10\"", StringComparison.Ordinal)).IsFailure);
        Assert.True(host.Source.ParseOutput(reference, json.Replace("\"sample\":\"$ 10\"", "\"sample\":\"$ 10\",\"x\":1", StringComparison.Ordinal)).IsFailure);
        // A stored proposal whose reading or standard the server would never have proposed does not read back.
        Assert.True(host.Source.ParseOutput(reference, Json(proposal with { DocumentReading = new(new string('r', FormDocumentReading.MaxRecognitionLength + 1)) })).IsFailure);
        Assert.True(host.Source.ParseOutput(reference, Json(proposal with { DocumentReading = new("") })).IsFailure);
        Assert.True(host.Source.ParseOutput(reference, Json(proposal with { Standard = new("forms.transfer-receipt", "") })).IsFailure);
        Assert.True(host.Source.ParseOutput(reference, Json(proposal with { Questions = [proposal.Questions[0] with { Sample = "\u200B" }] })).IsFailure);
        var rejection = Json(new FormAuthoringRejection([new("documentReading", "noDocument")]));
        Assert.IsType<FormAuthoringRejection>(host.Source.ParseOutput(reference, rejection).Value);
        // Version 1's form still reads back as before.
        var first = await host.Source.ExecuteAsync(host.Request(ProposeForm, Json(new { name = "Patentes", prefix = "PAT", submitLabel = "",
            questions = new[] { new { label = "Patente", type = "shortText", required = true, options = Array.Empty<string>(), catalogId = (Guid?)null } } })), Ct);
        Assert.IsType<FormAuthoringFormProposal>(host.Source.ParseOutput(new(ProposeForm, 1), Json(first.Value.Payload as FormAuthoringFormProposal)).Value);
    }

    [Fact]
    public void The_draft_of_version_two_turns_the_reading_on_without_its_samples()
    {
        var proposal = new FormAuthoringDocumentFormProposal("es", "Comprobantes", "CRE", "Enviar",
            [new("Monto", "number", true, [], null, "$ 1.234,50")], new("Comprobantes de transferencias."), new("forms.transfer-receipt", "Comprobante de transferencia"));
        var draft = FormAuthoringProposalPolicy.Draft(Guid.NewGuid(), proposal);
        var reading = draft.Draft.DocumentReading!;
        Assert.Equal((true, "Comprobantes de transferencias."), (reading.Enabled, reading.Recognition));
        Assert.Empty(reading.Lookups);
        Assert.DoesNotContain("1.234,50", JsonSerializer.Serialize(draft, JsonSerializerOptions.Web), StringComparison.Ordinal);
        Assert.Null(FormAuthoringProposalPolicy.Draft(Guid.NewGuid(), proposal with { DocumentReading = null }).Draft.DocumentReading);
    }
}
