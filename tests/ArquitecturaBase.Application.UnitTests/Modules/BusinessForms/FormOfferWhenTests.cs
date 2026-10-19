using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormOfferWhenTests
{
    [Fact]
    public void Offer_instructions_are_trimmed_and_have_no_translation_requirement()
    {
        var request = Request("  Cuando cuenta una visita.  ");
        Assert.True(new SaveFormDraftRequestValidator().Validate(request).IsValid);
        var normalized = FormDraftPolicy.Normalize(request);
        Assert.Equal("Cuando cuenta una visita.", normalized.OfferWhen);
        Assert.False(normalized.Name.ContainsKey("en"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n\t ")]
    public void Missing_or_blank_instructions_disable_offering(string? offerWhen) =>
        Assert.Null(FormDraftPolicy.Normalize(Request(offerWhen)).OfferWhen);

    [Theory]
    [InlineData("\0")]
    [InlineData("visita\u200B")]
    public void Non_storable_or_invisible_control_instructions_are_rejected_at_their_path(string value) =>
        Assert.Contains(new SaveFormDraftRequestValidator().Validate(Request(value)).Errors, error => error.PropertyName == "offerWhen");

    [Fact]
    public void An_unpaired_surrogate_is_rejected_before_persistence() =>
        Assert.Contains(new SaveFormDraftRequestValidator().Validate(Request(new string((char)0xD800, 1))).Errors,
            error => error.PropertyName == "offerWhen");

    [Fact]
    public void Instructions_are_limited_to_one_thousand_utf16_units()
    {
        var validator = new SaveFormDraftRequestValidator();
        Assert.True(validator.Validate(Request(new string('a', 1000))).IsValid);
        Assert.Contains(validator.Validate(Request(new string('a', 1001))).Errors, error => error.PropertyName == "offerWhen");
    }

    [Fact]
    public void Instructions_do_not_change_the_schema_or_bilingual_publication_rules()
    {
        var request = FormDraftPolicy.Normalize(Request("Cuando cuenta una visita."));
        var resolved = FormDraftPolicy.Resolve(request, null, FormRetiredIdentities.Empty).Value;
        Assert.DoesNotContain("Cuando", FormDraftPolicy.Serialize(new FormQuestionSchema(1, resolved.Draft.Fields)), StringComparison.Ordinal);
        var definition = new FormDefinitionResponse(Guid.CreateVersion7(), request.Name, request.Prefix, false, 1, resolved.Draft, request.OfferWhen);
        Assert.Contains(FormPublicationPolicy.CheckCultures(definition).Single(culture => culture.Culture == "en").Missing, issue => issue.Path == "name.en");
        Assert.DoesNotContain(FormPublicationPolicy.MissingTranslations(definition), issue => issue.Path.StartsWith("offerWhen", StringComparison.Ordinal));
    }

    [Fact]
    public void Instructions_are_not_part_of_the_publication_snapshot_or_fingerprint()
    {
        var request = FormDraftPolicy.Normalize(Request("Primera instrucción"));
        var draft = FormDraftPolicy.Resolve(request, null, FormRetiredIdentities.Empty).Value.Draft;
        var definition = new FormDefinitionResponse(Guid.CreateVersion7(), request.Name, request.Prefix, false, 1, draft, request.OfferWhen);
        var changed = definition with { OfferWhen = "Otra instrucción" };
        Assert.Equal(FormPublicationSnapshotPolicy.Fingerprint(definition, []), FormPublicationSnapshotPolicy.Fingerprint(changed, []));
        Assert.Equal(FormDraftPolicy.Serialize(FormPublicationSnapshotPolicy.ChannelRequest(definition)),
            FormDraftPolicy.Serialize(FormPublicationSnapshotPolicy.ChannelRequest(changed)));
    }

    [Fact]
    public void Disabled_instructions_preserve_the_request_bytes_used_by_existing_receipts()
    {
        var request = FormDraftPolicy.Normalize(Request(null));
        var existingShape = new { request.RequestId, request.Name, request.Prefix, request.Draft, request.ExpectedRevision };
        Assert.Equal(FormDraftPolicy.Serialize(existingShape), FormDraftPolicy.Serialize(request));
    }

    private static SaveFormDraftRequest Request(string? offerWhen) => new(Guid.CreateVersion7(),
        new Dictionary<string, string> { ["es"] = "Visita" }, "VISIT",
        new(1, "es", new Dictionary<string, string> { ["es"] = "Enviar" }, []), OfferWhen: offerWhen);
}
