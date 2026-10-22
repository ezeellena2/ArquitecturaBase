using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormMultipleChoiceCaptureTests
{
    private static readonly Guid First = Guid.Parse("00000000-0000-7000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-7000-8000-000000000002");
    private static readonly Guid Third = Guid.Parse("00000000-0000-7000-8000-000000000003");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static IReadOnlyList<CanonicalFormChoice> Choices => [Choice(First, "Primera"), Choice(Second, "Segunda"), Choice(Third, "Tercera")];
    private static CanonicalFormChoice Choice(Guid id, string text) => new(id, new Dictionary<string,string> { ["es"] = text });
    private static BusinessFormCaptureFixture Inline(int minimum = 1, int maximum = 3, bool required = true) =>
        new("multiChoice", required, selection: new(minimum, maximum), choices: Choices);
    private static string Array(params Guid[] options) => JsonSerializer.Serialize(options);
    private static StructuredRecordCaptureRequest Request(BusinessFormCaptureFixture f, string raw) => f.Request(new Dictionary<string,string> { [f.Key] = raw });
    private static Guid Field(BusinessFormCaptureFixture f) => Guid.ParseExact(f.Key[6..], "N");

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Own_options_are_stored_in_selection_order_with_one_real_array_per_question(int count)
    {
        var f = Inline();
        var selected = new[] { Third, First, Second }.Take(count).ToArray();
        var result = await f.Sink.CaptureAsync(Request(f, Array(selected)), Ct);
        Assert.Equal("Completed", result.State);
        var record = Assert.Single(f.Repository.Records);
        var values = f.Repository.Values.OrderBy(value => value.Position).ToArray();
        Assert.Equal(Enumerable.Range(0, count), values.Select(value => value.Position));
        Assert.Equal(selected, values.Select(value => value.ChoiceId!.Value));
        Assert.All(values, value =>
        {
            Assert.Equal("multiChoice", value.Type);
            Assert.Equal(Field(f), value.FieldId);
            Assert.Equal(record.Id, value.RecordId);
            Assert.Null(value.TextValue);
            Assert.Null(value.DecimalValue);
            Assert.Null(value.DateValue);
            Assert.Null(value.BooleanValue);
        });
        using var json = JsonDocument.Parse(record.ValuesJson);
        var answer = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(Field(f), answer.GetProperty("fieldId").GetGuid());
        Assert.Equal(JsonValueKind.Array, answer.GetProperty("value").ValueKind);
        Assert.Equal(selected, answer.GetProperty("value").EnumerateArray().Select(value => value.GetGuid()));
        Assert.Empty(f.Repository.References);
        Assert.Equal(1, f.Definition.LastRecordNumber);
        Assert.Single(f.Repository.Receipts);
    }

    [Fact]
    public async Task Twenty_selections_use_positions_zero_through_nineteen_without_truncation()
    {
        var options = Enumerable.Range(1, 20).Select(index => Choice(Guid.NewGuid(), "Opción " + index)).ToArray();
        var selected = options.Select(option => option.ChoiceId).Reverse().ToArray();
        var f = new BusinessFormCaptureFixture("multiChoice", true, selection: new(1, 20), choices: options);
        var result = await f.Sink.CaptureAsync(Request(f, Array(selected)), Ct);
        Assert.Equal("Completed", result.State);
        Assert.Equal(20, f.Repository.Values.Count);
        Assert.Equal(Enumerable.Range(0, 20), f.Repository.Values.OrderBy(value => value.Position).Select(value => value.Position));
        Assert.Equal(selected, f.Repository.Values.OrderBy(value => value.Position).Select(value => value.ChoiceId!.Value));
        using var json = JsonDocument.Parse(Assert.Single(f.Repository.Records).ValuesJson);
        Assert.Equal(20, Assert.Single(json.RootElement.EnumerateArray()).GetProperty("value").GetArrayLength());
    }

    [Theory]
    [InlineData(1, 3, 0)]
    [InlineData(2, 3, 1)]
    [InlineData(1, 2, 3)]
    public async Task A_selection_outside_the_published_bounds_rejects_the_whole_capture(int minimum, int maximum, int count)
    {
        var f = Inline(minimum, maximum);
        var selected = new[] { First, Second, Third }.Take(count).ToArray();
        var result = await f.Sink.CaptureAsync(Request(f, Array(selected)), Ct);
        Rejected(f, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_optional_multiple_choice_may_be_omitted_or_empty_without_creating_values(bool supplied)
    {
        var f = Inline(0, 3, required: false);
        var request = supplied ? Request(f, "[]") : f.Request(new Dictionary<string,string>());
        var result = await f.Sink.CaptureAsync(request, Ct);
        Assert.Equal("Completed", result.State);
        var record = Assert.Single(f.Repository.Records);
        Assert.Empty(f.Repository.Values);
        Assert.Empty(f.Repository.References);
        using var json = JsonDocument.Parse(record.ValuesJson);
        if (supplied)
        {
            var answer = Assert.Single(json.RootElement.EnumerateArray());
            Assert.Equal(Field(f), answer.GetProperty("fieldId").GetGuid());
            Assert.Equal(JsonValueKind.Array, answer.GetProperty("value").ValueKind);
            Assert.Equal(0, answer.GetProperty("value").GetArrayLength());
        }
        else Assert.Empty(json.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task A_required_multiple_choice_cannot_be_omitted()
    {
        var f = Inline();
        Rejected(f, await f.Sink.CaptureAsync(f.Request(new Dictionary<string,string>()), Ct));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("[1]")]
    [InlineData("[null]")]
    [InlineData("[true]")]
    [InlineData("[{}]")]
    [InlineData("[[\"00000000-0000-7000-8000-000000000001\"]]")]
    [InlineData("[\"not-a-guid\"]")]
    [InlineData("[\"00000000000070008000000000000001\"]")]
    [InlineData("[\"00000000-0000-0000-0000-000000000000\"]")]
    [InlineData("[\"00000000-0000-7000-8000-000000000001\",null]")]
    [InlineData("[")]
    public async Task Only_an_array_of_complete_option_identifiers_is_accepted(string raw)
    {
        var f = Inline();
        Rejected(f, await f.Sink.CaptureAsync(Request(f, raw), Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"\"")]
    public async Task Optional_multiple_choice_still_requires_an_array_when_an_answer_is_supplied(string raw)
    {
        var f = Inline(0, 3, required: false);
        Rejected(f, await f.Sink.CaptureAsync(Request(f, raw), Ct));
    }

    [Fact]
    public async Task The_single_choice_scalar_contract_cannot_be_used_for_multiple_choice()
    {
        var f = Inline();
        Rejected(f, await f.Sink.CaptureAsync(Request(f, First.ToString("D")), Ct));
    }

    [Fact]
    public async Task Repeating_an_option_rejects_instead_of_silently_deduplicating_it()
    {
        var f = Inline();
        Rejected(f, await f.Sink.CaptureAsync(Request(f, Array(First, First)), Ct));
    }

    [Fact]
    public async Task An_option_outside_the_published_own_options_rejects_the_whole_capture()
    {
        var f = Inline();
        Rejected(f, await f.Sink.CaptureAsync(Request(f, Array(First, Guid.NewGuid())), Ct));
    }

    [Theory]
    [InlineData("Manual")]
    [InlineData("Filters")]
    public async Task Each_selected_catalog_option_has_its_own_sealed_reference_and_value_position(string kind)
    {
        var catalog = Guid.NewGuid();
        var f = new BusinessFormCaptureFixture("multiChoice", true, catalog, selection: new(1, 3));
        f.Repository.ItemActive = kind == "Manual";
        var offer = Offer(f, catalog, kind);
        var request = Request(f, Array(Third, First)) with { Catalogs = [offer] };
        var result = await f.Sink.CaptureAsync(request, Ct);
        Assert.Equal("Completed", result.State);
        Assert.Equal(new[] { Third, First }, f.Repository.Values.OrderBy(value => value.Position).Select(value => value.ChoiceId!.Value));
        Assert.Equal(2, f.Repository.References.Count);
        foreach (var selected in new[] { First, Third })
        {
            var original = offer.Options.Single(option => option.Id == selected);
            var reference = Assert.Single(f.Repository.References, value => value.ItemId == selected);
            Assert.Equal(Field(f), reference.FieldId);
            Assert.Equal(catalog, reference.CatalogId);
            Assert.Equal(7, reference.CatalogRevision);
            Assert.Equal(original.Text, reference.Text);
            Assert.Equal(original.Detail, reference.Detail);
            Assert.Equal("Catálogo ofrecido", reference.CatalogName);
            Assert.Equal(original.Value, reference.Value);
            Assert.Equal(kind == "Manual" ? "Integer" : null, reference.ValueType);
        }
        if (kind == "Manual")
        {
            Assert.Equal(catalog, Assert.Single(f.Repository.CatalogLocks));
            Assert.Equal(new[] { First, Third }, f.Repository.ActivityReads.Select(value => value.ItemId).Order());
        }
        else
        {
            Assert.Empty(f.Repository.CatalogLocks);
            Assert.Empty(f.Repository.ActivityReads);
        }
    }

    [Fact]
    public async Task A_single_inactive_selected_manual_item_rejects_every_option_without_numbering()
    {
        var catalog = Guid.NewGuid();
        var f = new BusinessFormCaptureFixture("multiChoice", true, catalog, selection: new(1, 3));
        f.Repository.InactiveItems.Add(First);
        var result = await f.Sink.CaptureAsync(Request(f, Array(Third, First)) with { Catalogs = [Offer(f, catalog, "Manual")] }, Ct);
        Rejected(f, result, FormRecordErrors.CatalogItemInactive.Code);
        Assert.Equal(Field(f), result.RejectedFieldId);
        Assert.Contains(f.Repository.ActivityReads, value => value.ItemId == First);
        Assert.Equal(catalog, Assert.Single(f.Repository.CatalogLocks));
    }

    [Fact]
    public async Task Inactive_unselected_items_do_not_invalidate_the_options_that_were_selected()
    {
        var catalog = Guid.NewGuid();
        var f = new BusinessFormCaptureFixture("multiChoice", true, catalog, selection: new(1, 3));
        f.Repository.InactiveItems.Add(Second);
        var result = await f.Sink.CaptureAsync(Request(f, Array(Third, First)) with { Catalogs = [Offer(f, catalog, "Manual")] }, Ct);
        Assert.Equal("Completed", result.State);
        Assert.DoesNotContain(f.Repository.ActivityReads, value => value.ItemId == Second);
        Assert.Equal(2, f.Repository.References.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_activity_never_substitutes_for_a_missing_or_incomplete_session_offer(bool missingOffer)
    {
        var catalog = Guid.NewGuid();
        var f = new BusinessFormCaptureFixture("multiChoice", true, catalog, selection: new(1, 3));
        var request = Request(f, Array(First, Third));
        if (!missingOffer)
        {
            var offer = Offer(f, catalog, "Manual");
            request = request with { Catalogs = [offer with { Options = [offer.Options[0]] }] };
        }
        Rejected(f, await f.Sink.CaptureAsync(request, Ct));
        Assert.Empty(f.Repository.ActivityReads);
    }

    [Fact]
    public async Task Replay_preserves_the_chosen_order_and_can_be_recovered_after_permission_revocation()
    {
        var f = Inline();
        var request = Request(f, Array(Third, First));
        var first = await f.Sink.CaptureAsync(request, Ct);
        Assert.Equal("Completed", first.State);
        Assert.Equal(first, await f.Sink.CaptureAsync(request, Ct));
        var changed = await f.Sink.CaptureAsync(request with { Answers = new Dictionary<string,string> { [f.Key] = Array(First, Third) } }, Ct);
        Assert.Equal("Rejected", changed.State);
        Assert.Equal(FormRecordErrors.ReplayConflict.Code, changed.ErrorCode);
        f.Permission.Allowed = false;
        Assert.Equal(first, await f.Sink.CaptureAsync(request, Ct));
        Assert.Single(f.Repository.Records);
        Assert.Equal(2, f.Repository.Values.Count);
        Assert.Equal(1, f.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task A_changed_offer_on_replay_does_not_replace_any_selected_reference()
    {
        var catalog = Guid.NewGuid();
        var f = new BusinessFormCaptureFixture("multiChoice", true, catalog, selection: new(1, 3));
        var offer = Offer(f, catalog, "Manual");
        var request = Request(f, Array(Third, First)) with { Catalogs = [offer] };
        Assert.Equal("Completed", (await f.Sink.CaptureAsync(request, Ct)).State);
        var changed = await f.Sink.CaptureAsync(request with { Catalogs = [offer with { Revision = 8 }] }, Ct);
        Assert.Equal(FormRecordErrors.ReplayConflict.Code, changed.ErrorCode);
        Assert.Equal(2, f.Repository.References.Count);
        Assert.All(f.Repository.References, reference => Assert.Equal(7, reference.CatalogRevision));
        Assert.Equal(1, f.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task Existing_single_choice_keeps_a_scalar_json_value_and_position_zero()
    {
        var f = new BusinessFormCaptureFixture("choice", true, choices: Choices);
        var request = Request(f, First.ToString("D"));
        var result = await f.Sink.CaptureAsync(request, Ct);
        Assert.Equal("Completed", result.State);
        var row = Assert.Single(f.Repository.Values);
        Assert.Equal(0, row.Position);
        Assert.Equal("choice", row.Type);
        Assert.Equal(First, row.ChoiceId);
        Assert.Equal("[{\"fieldId\":\"" + Field(f).ToString("D") + "\",\"value\":\"" + First.ToString("D") + "\"}]", Assert.Single(f.Repository.Records).ValuesJson);
        Assert.DoesNotContain("selection", f.Version.SchemaJson, StringComparison.Ordinal);
        f.Permission.Allowed = false;
        Assert.Equal(result, await f.Sink.CaptureAsync(request, Ct));
        Assert.Single(f.Repository.Values);
    }

    private static StructuredFormCatalogOffer Offer(BusinessFormCaptureFixture f, Guid catalog, string kind) => new(Field(f), f.Key,
        "Opciones", catalog, 7, kind, "Catálogo ofrecido",
        [new(First, "Primera ofrecida", "Detalle A", Value: kind == "Manual" ? "10" : null),
         new(Second, "Segunda ofrecida", "Detalle B", Value: kind == "Manual" ? "20" : null),
         new(Third, "Tercera ofrecida", "Detalle C", Value: kind == "Manual" ? "30" : null)],
        ValueType: kind == "Manual" ? "Integer" : null);

    private static void Rejected(BusinessFormCaptureFixture f, StructuredRecordCaptureResponse result, string? code = null)
    {
        Assert.Equal("Rejected", result.State);
        Assert.Equal(code ?? FormRecordErrors.InvalidResponse.Code, result.ErrorCode);
        Assert.Empty(f.Repository.Records);
        Assert.Empty(f.Repository.Values);
        Assert.Empty(f.Repository.References);
        Assert.Empty(f.Repository.FileReferences);
        Assert.Empty(f.StoredFiles.Files);
        Assert.Empty(f.Actions.Runs.Runs);
        Assert.Equal(0, f.Definition.LastRecordNumber);
        Assert.Equal("Rejected", Assert.Single(f.Repository.Receipts).State);
    }
}
