using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// W3 (UC1-A): a short or long text outside the block can receive the location, the contact's name or the contact's phone that the
// person shares on their own while the form is open by a channel. It is saved in the question, omitted without it (drafts, versions
// and fingerprints keep their bytes), goes with the question and reaches the channel through the neutral contract.
public sealed class FormQuestionReceivesTests
{
    private static readonly Dictionary<string, string> Text = new() { ["es"] = "Entrega", ["en"] = "Delivery" };
    private const string Delivered = "0196f2a0-0000-7000-8000-0000000000d1";

    private static FormFieldInput Field(string localId, string type = "shortText", string? receives = null, bool? repeated = null,
        Guid? fieldId = null, Guid? replaces = null) => new(localId, fieldId, replaces, type, Text, null, false,
            type is "shortText" or "longText" ? 80 : null, null, Repeated: repeated, Receives: receives);

    private static FormFieldInput Block() => new("block", null, null, "group", Text, null, true, null, null, Rows: new(1, 10));

    private static SaveFormDraftRequest Request(params FormFieldInput[] fields) => new(Guid.NewGuid(), Text, "ENT", new(1, "es", Text, fields));

    private static (string Path, string Message)[] Errors(SaveFormDraftRequest request) =>
        new SaveFormDraftRequestValidator().Validate(request).Errors.Select(error => (error.PropertyName, error.ErrorMessage)).ToArray();

    private static string Message(Error error) => ErrorMessages.Get(error.Code);

    private static FormDraftResponse Resolve(SaveFormDraftRequest request, BusinessFormDefinition? existing = null)
    {
        Assert.Empty(Errors(request));
        var resolved = FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(request, supplyMissingSubmitLabel: existing is null), existing, FormRetiredIdentities.Empty);
        Assert.True(resolved.IsSuccess);
        return resolved.Value.Draft;
    }

    // The draft as saving keeps it, so the next request edits it.
    private static BusinessFormDefinition Saved(FormDraftResponse draft)
    {
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, Text["es"], FormDraftPolicy.Serialize(FormDraftPolicy.NormalizeName(Text)), "ENT", "es", FormDraftPolicy.Serialize(draft.SubmitLabel),
            FormDraftPolicy.Serialize(new FormQuestionSchema(1, draft.Fields, draft.Sections)), FormDraftPolicy.QuestionCount(draft.Fields), true).IsSuccess);
        return definition;
    }

    [Fact]
    public void Each_datum_goes_to_a_text_question_and_is_saved_in_it()
    {
        var request = Request(Field("address", receives: FormQuestionReceives.Location), Field("name", "longText", FormQuestionReceives.ContactName),
            Field("phone", receives: FormQuestionReceives.ContactPhone), Field("notes"));

        var draft = Resolve(request);

        Assert.Equal(new string?[] { "location", "contactName", "contactPhone", null }, draft.Fields.Select(field => field.Receives));
        var schema = FormDraftPolicy.Serialize(new FormQuestionSchema(1, draft.Fields));
        Assert.Contains("\"receives\":\"location\"", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("receives", FormDraftPolicy.Serialize(draft.Fields[3]), StringComparison.Ordinal);
        // With «Sólo en la app» the zone is hidden, and what was chosen stays saved.
        var appOnly = request with { Draft = request.Draft with { AppOnly = true } };
        Assert.Equal("location", Resolve(appOnly).Fields[0].Receives);
    }

    [Fact]
    public void A_draft_without_it_keeps_its_bytes()
    {
        var fieldId = Guid.Parse("0196f2a0-0000-7000-8000-000000000001");
        var field = new CanonicalFormField(fieldId, "field_" + fieldId.ToString("N"), "shortText", new Dictionary<string, string> { ["es"] = "Domicilio" },
            null, false, 80, null);

        Assert.Equal("{\"fieldId\":\"0196f2a0-0000-7000-8000-000000000001\",\"key\":\"field_0196f2a0000070008000000000000001\",\"type\":\"shortText\","
            + "\"label\":{\"es\":\"Domicilio\"},\"help\":null,\"required\":false,\"maxLength\":80,\"options\":null}", FormDraftPolicy.Serialize(field));
        var draft = Resolve(Request(Field("address"), Field("phone", "longText")));
        Assert.DoesNotContain("receives", FormDraftPolicy.Serialize(new FormQuestionSchema(1, draft.Fields)), StringComparison.Ordinal);
    }

    public static TheoryData<string, FormFieldInput[], Error> Refused => new()
    {
        // Only the three data the zone offers.
        { "draft.fields[0].receives", [Field("address", receives: "address")], FormDefinitionErrors.ContentInvalid },
        { "draft.fields[0].receives", [Field("address", receives: "Location")], FormDefinitionErrors.ContentInvalid },
        // Only a short or long text outside the block takes what the person shares.
        { "draft.fields[0].receives", [Field("amount", "decimal", FormQuestionReceives.Location)], FormDraftErrors.ReceivesQuestionInvalid },
        { "draft.fields[0].receives", [Field("when", "date", FormQuestionReceives.ContactPhone)], FormDraftErrors.ReceivesQuestionInvalid },
        { "draft.fields[0].receives", [Field("point", "location", FormQuestionReceives.Location)], FormDraftErrors.ReceivesQuestionInvalid },
        { "draft.fields[2].receives", [Field("notes"), Block(), Field("item", receives: FormQuestionReceives.ContactName, repeated: true)],
            FormDraftErrors.ReceivesQuestionInvalid },
        // Nor a question with a condition (CO1-A): the Flow never prefills it, so the form could not come back with what was shared.
        { "draft.fields[1].receives", [Field(Delivered, "boolean"), Field("phone", receives: FormQuestionReceives.ContactPhone) with
            { ShowWhen = new(Guid.Parse(Delivered), ["true"]) }], FormDraftErrors.ReceivesQuestionInvalid },
        // Nor one with a fixed validation (DOC1): a shared phone or name would never pass a CUIT check, or would pass as an alias.
        { "draft.fields[0].receives", [Field("tax", receives: FormQuestionReceives.ContactPhone) with { Validation = FormQuestionValidation.TaxId }],
            FormDraftErrors.ReceivesQuestionInvalid },
        { "draft.fields[0].receives", [Field("account", receives: FormQuestionReceives.ContactName) with { Validation = FormQuestionValidation.BankAccount }],
            FormDraftErrors.ReceivesQuestionInvalid },
        // Each datum, once in the draft: the later question is refused.
        { "draft.fields[2].receives", [Field("name", receives: FormQuestionReceives.ContactName), Field("notes"),
            Field("other", "longText", FormQuestionReceives.ContactName)], FormDraftErrors.ReceivesTaken },
        // With a «Ubicación» question the location goes there (W1-A).
        { "draft.fields[1].receives", [Field("point", "location"), Field("address", receives: FormQuestionReceives.Location)],
            FormDraftErrors.ReceivesLocationAsked },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_question_that_cannot_receive_it_is_refused_at_its_path(string path, FormFieldInput[] fields, Error error)
    {
        var errors = Errors(Request(fields));

        Assert.Contains((path, Message(error)), errors);
        Assert.Single(errors, error => error.Path.EndsWith(".receives", StringComparison.Ordinal));
    }

    [Fact]
    public void The_contact_still_goes_to_text_questions_of_a_form_that_asks_for_the_location()
    {
        var draft = Resolve(Request(Field("point", "location"), Field("name", receives: FormQuestionReceives.ContactName),
            Field("phone", receives: FormQuestionReceives.ContactPhone)));

        Assert.Equal(new string?[] { null, "contactName", "contactPhone" }, draft.Fields.Select(field => field.Receives));
    }

    [Fact]
    public void It_goes_with_its_question_and_a_type_change_does_not_inherit_it()
    {
        var first = Resolve(Request(Field("address", receives: FormQuestionReceives.Location), Field("phone", receives: FormQuestionReceives.ContactPhone),
            Field("notes")));
        var saved = Saved(first);
        var (address, phone, notes) = (first.Fields[0].FieldId, first.Fields[1].FieldId, first.Fields[2].FieldId);
        SaveFormDraftRequest Edit(params FormFieldInput[] fields) => Request(fields) with { ExpectedRevision = 1 };

        // Renamed, the question keeps it, as the editor sends it back.
        var renamed = Resolve(Edit(Field("address", receives: FormQuestionReceives.Location, fieldId: address) with
            { Label = new Dictionary<string, string> { ["es"] = "Dirección de entrega", ["en"] = "Delivery address" } },
            Field("phone", receives: FormQuestionReceives.ContactPhone, fieldId: phone), Field("notes", fieldId: notes)), saved);
        Assert.Equal(new string?[] { "location", "contactPhone", null }, renamed.Fields.Select(field => field.Receives));

        // Resolve does not copy it from the previous question: the new identity of a type change carries only what the editor sends,
        // here nothing, also into another text type. A removed question takes it along.
        var changed = Resolve(Edit(Field("address", "longText", replaces: address), Field("notes", fieldId: notes)), saved);
        Assert.NotEqual(address, changed.Fields[0].FieldId);
        Assert.All(changed.Fields, field => Assert.Null(field.Receives));
        Assert.DoesNotContain("receives", FormDraftPolicy.Serialize(new FormQuestionSchema(1, changed.Fields)), StringComparison.Ordinal);

        // The new identity of a type change into a type that cannot receive it is refused while it still carries it.
        Assert.Contains(("draft.fields[0].receives", Message(FormDraftErrors.ReceivesQuestionInvalid)),
            Errors(Edit(Field("address", "decimal", FormQuestionReceives.Location, replaces: address), Field("notes", fieldId: notes))));
        // Freed by the removed question, the datum goes to another one.
        var moved = Resolve(Edit(Field("notes", receives: FormQuestionReceives.ContactPhone, fieldId: notes)), saved);
        Assert.Equal("contactPhone", Assert.Single(moved.Fields).Receives);
    }

    [Fact]
    public void The_publication_check_compares_and_seals_it_and_the_channel_receives_it()
    {
        var draft = Resolve(Request(Field("address", receives: FormQuestionReceives.Location), Field("notes")));
        var saved = FormDraftPolicy.Definition(Saved(draft));
        Assert.Equal("location", saved.Draft.Fields[0].Receives);

        SaveFormDraftRequest Check(string? receives) => FormDraftPolicy.Normalize(Request(
            Field("address", receives: receives, fieldId: saved.Draft.Fields[0].FieldId), Field("notes", fieldId: saved.Draft.Fields[1].FieldId)));
        Assert.True(FormPublicationSnapshotPolicy.Matches(Check(FormQuestionReceives.Location), saved));
        Assert.False(FormPublicationSnapshotPolicy.Matches(Check(null), saved));
        Assert.False(FormPublicationSnapshotPolicy.Matches(Check(FormQuestionReceives.ContactName), saved));

        // It is part of what the check seals.
        var without = saved with { Draft = saved.Draft with { Fields = [saved.Draft.Fields[0] with { Receives = null }, saved.Draft.Fields[1]] } };
        Assert.NotEqual(FormPublicationSnapshotPolicy.Fingerprint(saved, []), FormPublicationSnapshotPolicy.Fingerprint(without, []));

        var questions = FormPublicationSnapshotPolicy.ChannelRequest(saved).Questions;
        Assert.Equal(new string?[] { "location", null }, questions.Select(question => question.Receives));
        Assert.Contains("\"receives\":\"location\"", FormDraftPolicy.Serialize(questions[0]), StringComparison.Ordinal);
        Assert.DoesNotContain("receives", FormDraftPolicy.Serialize(questions[1]), StringComparison.Ordinal);
    }

    [Fact]
    public void A_question_bound_to_the_standard_can_receive_it()
    {
        var bound = Resolve(Request(Field("phone"))).Fields[0];
        var bindings = FormDraftPolicy.Serialize(new Dictionary<string, Guid> { ["contactPhone"] = bound.FieldId });

        Assert.True(FormStandardPolicy.CheckBound(bindings, [bound], [bound with { Receives = FormQuestionReceives.ContactPhone }]).IsSuccess);
        Assert.Equal(FormDraftErrors.BoundQuestionChanged.Code, FormStandardPolicy.CheckBound(bindings, [bound], [bound with { MaxLength = 120 }]).Error.Code);
    }
}
