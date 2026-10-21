using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormResponseChainTests
{
    [Fact]
    public async Task Detail_returns_the_real_chain_in_order_with_one_batch_read()
    {
        var f = new Fixture(); var first = f.Reader.ExportRows[0]; var second = f.Reader.ExportRows[1];
        f.Reader.Chain = [new(second.Record.RecordId, second.Record.DefinitionId, first.Record.RecordId, "NEXT", 1, second.Record.NameJson, first.SubmittedAtUtc.AddMinutes(1)) { Version = second.Version },
            new(first.Record.RecordId, first.Record.DefinitionId, null, "VISIT", 12, first.Record.NameJson, first.SubmittedAtUtc) { Version = first.Version }];
        var result = await f.Service.GetAsync(second.Record.RecordId, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal([first.Record.RecordId, second.Record.RecordId], result.Value.Chain.Select(record => record.Id));
        Assert.Equal(["VISIT-000012", "NEXT-000001"], result.Value.Chain.Select(record => record.Number));
        Assert.Equal(1, f.Reader.ChainReads);
    }

    [Fact]
    public async Task A_single_unlinked_capture_does_not_claim_a_suggestion_chain()
    {
        var f = new Fixture(); var row = f.Reader.ExportRows[0];
        f.Reader.Chain = [new(row.Record.RecordId, row.Record.DefinitionId, null, "VISIT", 1, row.Record.NameJson, row.SubmittedAtUtc)];
        var result = await f.Service.GetAsync(row.Record.RecordId, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.Empty(result.Value.Chain);
    }

    [Fact]
    public async Task No_record_permission_prevents_both_the_record_and_chain_queries()
    {
        var f = new Fixture(); f.Permissions.Permissions.Clear();
        var result = await f.Service.GetAsync(f.Reader.ExportRows[0].Record.RecordId, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure); Assert.Equal(0, f.Reader.Reads); Assert.Equal(0, f.Reader.ChainReads);
    }

    [Fact]
    public async Task Revocation_while_reading_the_chain_discards_the_whole_detail()
    {
        var f = new Fixture(); f.Reader.BeforeChainReturn = () => f.Permissions.Permissions.Clear();
        var result = await f.Service.GetAsync(f.Reader.ExportRows[0].Record.RecordId, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure); Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
    }

    [Fact]
    public async Task Detail_uses_the_captured_suggestion_mode_after_the_current_configuration_changes()
    {
        var f = new Fixture(); var row = f.Reader.ExportRows[0];
        var sourceRecordId = f.Reader.ExportRows[1].Record.RecordId;
        var suggestion = new FormSuggestion(Guid.NewGuid(), row.Record.DefinitionId,
            "Always", null, true, [], []);
        var historic = BusinessFormSuggestionSet.Create(f.Suggestions.Definition.Id, Guid.NewGuid(), 1, "es",
            FormDraftPolicy.Serialize(new[] { suggestion }));
        f.Suggestions.Sets.Add(historic);
        var current = BusinessFormSuggestionSet.Create(f.Suggestions.Definition.Id, historic.SourceVersionId, 2, "es",
            FormDraftPolicy.Serialize(new[] { suggestion with { Mode = "Assistant", When = "Cuando lo pida" } }));
        f.Suggestions.Sets.Add(current); f.Suggestions.Definition.SetActiveSuggestionSetId(current.Id);
        var origin = new StructuredRecordCaptureOrigin(
            StructuredRecordSenderKind.Assistant, null, "Sugerido después de la visita.", null, [], [],
            new(sourceRecordId, suggestion.Id, historic.Id, []));
        f.Reader.ExportRows = [row with { CaptureOriginJson = FormDraftPolicy.Serialize(new FormRecordCaptureOriginSnapshot(origin, [], [])) }, .. f.Reader.ExportRows.Skip(1)];
        var result = await f.Service.GetAsync(row.Record.RecordId, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.NotNull(result.Value.Assistance); Assert.NotNull(result.Value.Assistance.Suggestion);
        Assert.Equal("Always", result.Value.Assistance.Suggestion.Mode);
        Assert.Equal(sourceRecordId, result.Value.Assistance.Suggestion.OriginRecordId);
        Assert.Equal(suggestion.Id, result.Value.Assistance.Suggestion.SuggestionId); Assert.Equal(1, f.Suggestions.SetReads);
    }

    [Fact]
    public async Task Revocation_while_loading_the_historical_suggestion_discards_the_whole_detail()
    {
        var f = new Fixture(); var row = f.Reader.ExportRows[0];
        var suggestion = new FormSuggestion(Guid.NewGuid(), row.Record.DefinitionId, "Always", null, true, [], []);
        var historic = BusinessFormSuggestionSet.Create(f.Suggestions.Definition.Id, Guid.NewGuid(), 1, "es", FormDraftPolicy.Serialize(new[] { suggestion }));
        f.Suggestions.Sets.Add(historic);
        var origin = new StructuredRecordCaptureOrigin(
            StructuredRecordSenderKind.Assistant, null, null, null, [], [],
            new(f.Reader.ExportRows[1].Record.RecordId, suggestion.Id, historic.Id, []));
        f.Reader.ExportRows = [row with { CaptureOriginJson = FormDraftPolicy.Serialize(new FormRecordCaptureOriginSnapshot(origin, [], [])) }, .. f.Reader.ExportRows.Skip(1)];
        f.Suggestions.BeforeSetReturn = () => f.Permissions.Permissions.Clear();
        var result = await f.Service.GetAsync(row.Record.RecordId, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure); Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
    }

    private sealed class Fixture
    {
        public FakeFormResponseReader Reader { get; } = new();
        public FakeCurrentUser User { get; } = new() { UserId = Guid.NewGuid() };
        public FakePermissionService Permissions { get; } = new();
        public FormSuggestionFixture Suggestions { get; } = new();
        public FormResponseService Service { get; }
        public Fixture()
        {
            Reader.ArrangeExport(2); Permissions.Permissions[User.UserId!.Value] = [BusinessFormsPermissions.RecordsRead];
            Service = new(Reader, new(User, Permissions), RequestValidators.For(new ListFormRecordsRequestValidator()),
                NullLogger<FormResponseService>.Instance, new([]), Suggestions);
        }
    }
}
