using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormResponseServiceTests
{
    [Theory]
    [InlineData("list")]
    [InlineData("facets")]
    [InlineData("detail")]
    public async Task Revocation_while_reading_discards_rows_counts_and_detail(string operation)
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var reader = new FakeFormResponseReader { BeforeReturn = () => permissions.Permissions.Clear() };
        reader.ArrangeExport(1);
        var service = Create(reader, user, permissions);
        Result result = operation switch
        {
            "list" => await service.ListAsync(reader.Definition!.Id, new(), CancellationToken.None),
            "facets" => await service.CountFacetsAsync(reader.Definition!.Id, new(), CancellationToken.None),
            _ => await service.GetAsync(reader.ExportRows[0].Record.RecordId, CancellationToken.None),
        };
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
    }
    [Theory]
    [InlineData("list")]
    [InlineData("facets")]
    [InlineData("detail")]
    public async Task Losing_the_load_permission_while_reading_discards_the_own_rows_counts_and_detail(string operation)
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsCreate];
        var reader = new FakeFormResponseReader { BeforeReturn = () => permissions.Permissions.Clear() };
        reader.ArrangeExport(1);
        reader.ExportRows = [reader.ExportRows[0] with { Actor = new(user.UserId.Value, "Employee") }];
        var service = Create(reader, user, permissions);
        Result result = operation switch
        {
            "list" => await service.ListAsync(reader.Definition!.Id, new(), CancellationToken.None),
            "facets" => await service.CountFacetsAsync(reader.Definition!.Id, new(), CancellationToken.None),
            _ => await service.GetAsync(reader.ExportRows[0].Record.RecordId, CancellationToken.None),
        };
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
    }
    [Fact]
    public async Task Whoever_only_loads_reads_the_sheet_counts_and_navigation_of_their_own_responses()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsCreate];
        var reader = new FakeFormResponseReader();
        reader.ArrangeExport(1);
        var service = Create(reader, user, permissions);
        var someoneElse = new ListFormRecordsRequest { ActorIds = [Guid.NewGuid()] };

        Assert.True((await service.ListAsync(reader.Definition!.Id, someoneElse, CancellationToken.None)).IsSuccess);
        Assert.Equal(user.UserId, reader.ListRequest!.OwnerUserId);
        Assert.Equal(someoneElse.ActorIds, reader.ListRequest.ActorIds);
        Assert.True((await service.CountFacetsAsync(reader.Definition.Id, new() { Filter = someoneElse, Dimension = "actor" }, CancellationToken.None)).IsSuccess);
        Assert.Equal(user.UserId, reader.FacetRequest!.Filter.OwnerUserId);
        await service.FindRecordNavigationAsync(reader.Definition.Id, reader.ExportRows[0].Record.RecordId, someoneElse, CancellationToken.None);
        Assert.Equal(user.UserId, reader.NavigationRequest!.OwnerUserId);
    }
    [Fact]
    public async Task Whoever_reads_responses_keeps_the_people_they_filter_by()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsCreate];
        var reader = new FakeFormResponseReader();
        reader.ArrangeExport(1);
        var person = Guid.NewGuid();

        Assert.True((await Create(reader, user, permissions).ListAsync(reader.Definition!.Id, new() { ActorIds = [person] }, CancellationToken.None)).IsSuccess);
        Assert.Equal([person], reader.ListRequest!.ActorIds);
        Assert.Null(reader.ListRequest.OwnerUserId);
    }
    [Fact]
    public async Task Someone_elses_response_is_not_found_for_whoever_only_loads()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsCreate];
        var reader = new FakeFormResponseReader();
        reader.ArrangeExport(2);
        reader.ExportRows = [reader.ExportRows[0] with { Actor = new(user.UserId.Value, "Employee") }, reader.ExportRows[1]];
        var service = Create(reader, user, permissions);

        Assert.True((await service.GetAsync(reader.ExportRows[0].Record.RecordId, CancellationToken.None)).IsSuccess);
        Assert.Equal(FormRecordErrors.NotFound, (await service.GetAsync(reader.ExportRows[1].Record.RecordId, CancellationToken.None)).Error);
        Assert.Equal(1, reader.ChainReads);
    }
    [Fact]
    public async Task Without_reading_or_loading_responses_the_sheet_is_forbidden()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.ApprovalsDecide];
        var reader = new FakeFormResponseReader();
        reader.ArrangeExport(1);

        Assert.Equal(FormDefinitionErrors.Forbidden, (await Create(reader, user, permissions).ListAsync(reader.Definition!.Id, new(), CancellationToken.None)).Error);
        Assert.Equal(0, reader.Reads);
    }
    [Fact]
    public async Task Invalid_pagination_never_reads_the_database()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService(); permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var reader = new FakeFormResponseReader();
        Assert.True((await Create(reader, user, permissions).ListAsync(Guid.NewGuid(), new() { Page = 0 }, CancellationToken.None)).IsFailure);
        Assert.Equal(0, reader.Reads);
    }
    [Fact]
    public async Task List_projects_rows_using_the_definition_schema_for_their_version()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var reader = new FakeFormResponseReader();
        reader.ArrangeExport(2);
        reader.ExportRows = reader.ExportRows.Select(row => row with { Record = row.Record with { SchemaJson = "not-read" } }).ToArray();
        var result = await Create(reader, user, permissions).ListAsync(reader.Definition!.Id, new(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "T-000001", "T-000002" }, result.Value.Records.Items.Select(row => row.Number));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sheet_preserves_approval_visibility_and_the_state_of_each_response(bool hasApproval)
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var reader = new FakeFormResponseReader();
        reader.ArrangeExport(2);
        reader.Definition = reader.Definition! with { HasApproval = hasApproval };
        reader.ExportRows = [reader.ExportRows[0] with { State = "PendingApproval" }, reader.ExportRows[1] with { State = "Rejected" }];
        var result = await Create(reader, user, permissions).ListAsync(reader.Definition.Id, new(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(hasApproval, result.Value.HasApproval);
        Assert.Equal(new[] { "PendingApproval", "Rejected" }, result.Value.Records.Items.Select(row => row.State));
    }

    [Fact]
    public async Task Missing_record_returns_the_same_unavailable_error()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService(); permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var result = await Create(new(), user, permissions).GetAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(FormRecordErrors.NotFound, result.Error);
    }
    [Fact]
    public async Task List_does_not_infer_pdf_presence_from_an_approved_state()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var reader = new FakeFormResponseReader();
        reader.ArrangeExport(3);
        reader.Definition = reader.Definition! with { HasApproval = true };
        reader.ExportRows = reader.ExportRows.Select((row, index) =>
        {
            var stored = JsonSerializer.SerializeToNode(row with { State = index == 0 ? "Received" : "Approved" }, JsonSerializerOptions.Web)!;
            stored["hasPdf"] = index == 2;
            return JsonSerializer.Deserialize<FormResponseStoredRow>(stored.ToJsonString(), JsonSerializerOptions.Web)!;
        }).ToArray();

        var result = await Create(reader, user, permissions).ListAsync(reader.Definition.Id, new(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { false, false, true }, result.Value.Records.Items.Select(row =>
            JsonSerializer.SerializeToElement(row, JsonSerializerOptions.Web).GetProperty("hasPdf").GetBoolean()));
    }

    [Theory]
    [InlineData("choice")]
    [InlineData("presence")]
    public async Task Counts_accept_a_question_of_the_block_but_not_the_block_itself(string dimension)
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService(); permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var label = new Dictionary<string, string> { ["es"] = "Pregunta", ["en"] = "Question" };
        var block = new CanonicalFormField(Guid.NewGuid(), "items", "group", label, null, true, null, null, Rows: new(1, 10));
        var quantity = new CanonicalFormField(Guid.NewGuid(), "quantity", "decimal", label, null, true, null, null, Repeated: true);
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{\"es\":\"Formulario\",\"en\":\"Form\"}",
            JsonSerializer.Serialize(new FormVersionSchema(1, [block, quantity]), JsonSerializerOptions.Web), "es", ["es", "en"]);
        var reader = new FakeFormResponseReader { Definition = new(Guid.NewGuid(), "T", version.Id, [version]) };
        var service = Create(reader, user, permissions);

        var counted = await service.CountFacetsAsync(reader.Definition.Id, new() { Dimension = dimension, FieldId = quantity.FieldId }, CancellationToken.None);
        var onTheBlock = await service.CountFacetsAsync(reader.Definition.Id, new() { Dimension = dimension, FieldId = block.FieldId }, CancellationToken.None);

        Assert.True(counted.IsSuccess);
        Assert.Equal(quantity.FieldId, reader.FacetRequest!.FieldId);
        Assert.Equal(FormRecordErrors.FilterInvalid, onTheBlock.Error);
    }

    private static FormResponseService Create(FakeFormResponseReader reader, FakeCurrentUser user, FakePermissionService permissions) =>
        new(reader, new(user, permissions, reader), RequestValidators.For(new ListFormRecordsRequestValidator()), NullLogger<FormResponseService>.Instance, new([]), new FormSuggestionFixture(),
            new(user, permissions, new ArquitecturaBase.Application.UnitTests.TestDoubles.Users.InMemoryUserAccounts()));
}
