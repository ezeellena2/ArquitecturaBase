using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// DOC1: the fixed validations are of the question, not of the AI. A short text bound by a library standard brings its validation
// (an account or a CUIT), which only a bound question has and which does not change, like the rest of a bound question; every
// capture enforces it: an account whose digits do not add up is not saved, with its own code at its question.
public sealed class FormFixedValidationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly IReadOnlyDictionary<string, string> Text = new Dictionary<string, string> { ["es"] = "Cuenta" };

    private static SaveFormDraftRequest Request(params FormFieldInput[] fields) => new(Guid.NewGuid(), Text, "REC", new(1, "es", Text, fields));

    private static IEnumerable<string> Errors(SaveFormDraftRequest request) =>
        new SaveFormDraftRequestValidator().Validate(request).Errors.Select(error => error.PropertyName);

    [Theory]
    [InlineData("shortText", FormQuestionValidation.BankAccount, true)]
    [InlineData("shortText", FormQuestionValidation.TaxId, true)]
    [InlineData("shortText", "cbu", false)]
    [InlineData("shortText", "", false)]
    [InlineData("longText", FormQuestionValidation.BankAccount, false)]
    [InlineData("decimal", FormQuestionValidation.TaxId, false)]
    public void Only_a_short_text_has_a_known_validation(string type, string validation, bool valid)
    {
        var field = new FormFieldInput("account", null, null, type, Text, null, false, type == "decimal" ? null : 40, null, Validation: validation);

        Assert.Equal(!valid, Errors(Request(field)).Contains("draft.fields[0].validation"));
    }

    private static CanonicalFormField Field(string? validation = FormQuestionValidation.BankAccount, bool repeated = false)
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), "shortText", Text, null, false, 40, null, Repeated: repeated ? true : null, Validation: validation);
    }

    [Fact]
    public void Only_a_bound_question_has_a_validation_and_it_does_not_change()
    {
        var account = Field();
        var other = Field(FormQuestionValidation.TaxId);

        Assert.True(FormStandardPolicy.CheckDeclared([account.FieldId, other.FieldId], [account, other]).IsSuccess);
        var undeclared = FormStandardPolicy.CheckDeclared([account.FieldId], [account, other]);
        Assert.Equal(FormDraftErrors.QuestionConfigurationInvalid.Code, undeclared.Error.Code);
        Assert.Equal("draft.fields[1].validation", (undeclared.Error as ValidationError)?.Errors.Keys.Single());

        var bindings = FormDraftPolicy.Serialize(new Dictionary<string, Guid> { ["sourceAccount"] = account.FieldId });
        Assert.True(FormStandardPolicy.CheckBound(bindings, [account], [account with { Label = new Dictionary<string, string> { ["es"] = "Cuenta que paga" } }]).IsSuccess);
        foreach (var changed in new[] { account with { Validation = null }, account with { Validation = FormQuestionValidation.TaxId } })
        {
            var refused = FormStandardPolicy.CheckBound(bindings, [account], [changed]);
            Assert.Equal(FormDraftErrors.BoundQuestionChanged.Code, refused.Error.Code);
            Assert.Equal("draft.fields[0]", (refused.Error as ValidationError)?.Errors.Keys.Single());
        }
    }

    [Theory]
    [InlineData(FormQuestionValidation.BankAccount, "2850590940090418135201")]
    [InlineData(FormQuestionValidation.BankAccount, "0000003110001234567898")]
    [InlineData(FormQuestionValidation.BankAccount, "juan.perez.mp")]
    [InlineData(FormQuestionValidation.TaxId, "20-12345678-6")]
    [InlineData(FormQuestionValidation.TaxId, "27400001230")]
    public async Task A_capture_whose_fixed_validation_passes_is_saved_as_written(string validation, string value)
    {
        var f = new BusinessFormCaptureFixture([Field(validation)]);

        var result = await f.Sink.CaptureAsync(f.Request(new Dictionary<string, string> { [f.Key] = value }), Ct);

        Assert.Equal("Completed", result.State);
        Assert.Equal(value, Assert.Single(f.Repository.Values).TextValue);
    }

    [Theory]
    [InlineData(FormQuestionValidation.BankAccount, "2850590940090418135202", "Forms.Record.BankAccountInvalid")]
    [InlineData(FormQuestionValidation.BankAccount, "285059094009041813520", "Forms.Record.BankAccountInvalid")]
    [InlineData(FormQuestionValidation.BankAccount, "juan perez", "Forms.Record.BankAccountInvalid")]
    [InlineData(FormQuestionValidation.TaxId, "20-12345678-7", "Forms.Record.TaxIdInvalid")]
    [InlineData(FormQuestionValidation.TaxId, "2012345678", "Forms.Record.TaxIdInvalid")]
    public async Task A_capture_whose_fixed_validation_fails_is_rejected_at_its_question_without_a_response(string validation, string value, string code)
    {
        var question = Field(validation);
        var f = new BusinessFormCaptureFixture([question]);

        var result = await f.Sink.CaptureAsync(f.Request(new Dictionary<string, string> { [f.Key] = value }), Ct);

        Assert.Equal(("Rejected", code, (Guid?)question.FieldId, (int?)null), (result.State, result.ErrorCode, result.RejectedFieldId, result.RejectedRow));
        Assert.Empty(f.Repository.Records);
        Assert.Empty(f.Repository.Values);
    }

    // Task 9b: a channel may send the form again with what the person entered; only the capture that found the failure says it.
    [Fact]
    public async Task Only_the_capture_a_fixed_validation_rejects_says_so_and_its_replay_does_not()
    {
        var question = Field();
        var f = new BusinessFormCaptureFixture([question]);
        var request = f.Request(new Dictionary<string, string> { [f.Key] = "2850590940090418135202" });

        var rejected = await f.Sink.CaptureAsync(request, Ct);
        var replay = await f.Sink.CaptureAsync(request, Ct);
        var tooLong = await f.Sink.CaptureAsync(f.Request(new Dictionary<string, string> { [f.Key] = new string('1', 41) })
            with { SourceReceiptId = Guid.CreateVersion7() }, Ct);

        Assert.Equal(("Rejected", "Forms.Record.BankAccountInvalid", (bool?)true), (rejected.State, rejected.ErrorCode, rejected.FixedValidation));
        Assert.Equal(("Rejected", "Forms.Record.BankAccountInvalid", (Guid?)question.FieldId, (bool?)null),
            (replay.State, replay.ErrorCode, replay.RejectedFieldId, replay.FixedValidation));
        Assert.Equal(("Rejected", FormRecordErrors.InvalidResponse.Code, (bool?)null), (tooLong.State, tooLong.ErrorCode, tooLong.FixedValidation));
    }

    [Fact]
    public async Task An_optional_question_left_empty_is_not_validated()
    {
        var f = new BusinessFormCaptureFixture([Field()]);

        var result = await f.Sink.CaptureAsync(f.Request(new Dictionary<string, string>()), Ct);

        Assert.Equal("Completed", result.State);
    }

    [Fact]
    public async Task A_fixed_validation_inside_the_block_of_rows_names_the_item_that_fails()
    {
        var block = new CanonicalFormField(Guid.CreateVersion7(), "block", "group", Text, null, true, null, null, Rows: new(1, 5));
        var account = Field(repeated: true);
        var f = new BusinessFormCaptureFixture([block, account]);

        var result = await f.Sink.CaptureAsync(f.Request(new Dictionary<string, string>
        {
            [block.Key] = $$"""[{"{{account.Key}}":"2850590940090418135201"},{"{{account.Key}}":"2850590940090418135209"}]""",
        }), Ct);

        Assert.Equal(("Rejected", "Forms.Record.BankAccountInvalid", (Guid?)account.FieldId, (int?)1),
            (result.State, result.ErrorCode, result.RejectedFieldId, result.RejectedRow));
    }

    [Fact]
    public void The_failure_is_found_on_the_checked_answers_of_the_sealed_version()
    {
        var account = Field();
        var taxId = Field(FormQuestionValidation.TaxId);
        var schema = new FormVersionSchema(1, [account, taxId]);
        IReadOnlyList<FormRecordAnswer> Answers(string first, string second) =>
            FormRecordAnswerPolicy.Normalize(schema, new Dictionary<string, string> { [account.Key] = first, [taxId.Key] = second },
                Array.Empty<StructuredFormCatalogOffer>())!;

        Assert.Null(FormRecordAnswerPolicy.FindInvalid(schema, Answers("2850590940090418135201", "20123456786")));
        Assert.Equal(new FormRecordAnswerFailure(FormRecordErrors.TaxIdInvalid, taxId.FieldId, null),
            FormRecordAnswerPolicy.FindInvalid(schema, Answers("2850590940090418135201", "20123456787")));
        // The first question that fails, in the order of the version.
        Assert.Equal(new FormRecordAnswerFailure(FormRecordErrors.BankAccountInvalid, account.FieldId, null),
            FormRecordAnswerPolicy.FindInvalid(schema, Answers("2850590940090418135200", "20123456787")));
    }
}
