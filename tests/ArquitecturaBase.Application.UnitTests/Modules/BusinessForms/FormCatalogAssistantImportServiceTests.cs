using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Stage 2 of the assistant authoring: importing the spreadsheet a proposal mapped from the private copy of its turn, with the review,
// the confirmations, the locks and the single boundary of an upload.
public sealed class FormCatalogAssistantImportServiceTests
{
    private const string ProposeCatalog = BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId;
    private const string ProposeImport = BusinessFormAuthoringAssistantCapabilitySource.ProposeImportId;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_catalog_created_from_a_proposal_reviews_and_imports_the_copy_of_its_spreadsheet()
    {
        using var f = new FormCatalogImportFixture();
        f.ProposeCatalog();
        var copy = await f.HandAsync(f.Mapped);

        var preview = await f.Service.PreviewFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(), Ct);

        Assert.True(preview.IsSuccess);
        Assert.Equal(("Replace", copy.Sha256, "Hoja1", 2, 2), (preview.Value.Mode, preview.Value.SheetSha256, preview.Value.SheetName,
            preview.Value.RowsRead, preview.Value.Counts.Added));
        var phone = JsonSerializer.Deserialize<BusinessFormCatalogField[]>(f.Catalog.FieldsJson!, JsonSerializerOptions.Web)!.Single().Id.ToString("D");
        Assert.Equal([("20-1", "Transportes Sur"), ("20-2", "Logística Pampa")], preview.Value.Added.Select(row => (row.Code, row.Text)));
        Assert.Equal("11 5555-0001", preview.Value.Added[0].Fields![phone]);
        Assert.False(preview.Value.Added[1].Fields!.ContainsKey(phone));
        Assert.Equal((0, 0, 1, 0), (f.Work.Transactions, f.FileReads, f.CopyReads, f.Locks));
        // A later import proposal replaces a proposed catalog, and the other way around.
        Assert.Equal([ProposeCatalog, ProposeImport], f.Lookup.LastCapabilityGroup);

        var applied = await f.Service.ApplyFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(preview.Value.SheetSha256), Ct);

        Assert.True(applied.IsSuccess);
        Assert.Equal(["20-1", "20-2"], f.Items.Select(item => item.Code));
        Assert.Equal((copy.Sha256, 2L, 1, CommitPolicy.OnSuccess), (f.Catalog.LastImportSha256, f.Catalog.Revision, f.Work.Commits, f.Work.LastPolicy));
        Assert.Equal([f.Catalog.Id], f.LockedIds);
        // Importing is not approving: the catalog stays inactive and pending.
        Assert.Equal((false, BusinessFormCatalogProposalState.Pending), (f.Catalog.IsActive, f.Catalog.ProposalState));
        Assert.Equal(2, applied.Value.Catalog.ItemsCount);
    }

    // The lookup finds only the person's own turns: another person who manages catalogs does not see the proposal.
    [Fact]
    public async Task Another_person_cannot_import_someone_elses_proposal()
    {
        using var f = new FormCatalogImportFixture();
        f.ProposeCatalog();
        await f.HandAsync(f.Mapped);
        f.Lookup.OwnerUserId = Guid.NewGuid();

        var preview = await f.Service.PreviewFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(), Ct);
        var applied = await f.Service.ApplyFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(FormCatalogImportFixture.Sha256(f.Copy)), Ct);

        Assert.Equal((FormCatalogErrors.ProposalUnavailable, FormCatalogErrors.ProposalUnavailable), (preview.Error, applied.Error));
        Assert.Equal((0, 0, 0), (f.Files.ReadCount, f.CopyReads, f.Work.Transactions));
    }

    // Replacing, as uploading a file does: what the spreadsheet does not bring is deactivated, with the confirmation of always.
    [Fact]
    public async Task A_current_import_proposal_replaces_the_options_of_the_catalog_it_names()
    {
        using var f = new FormCatalogImportFixture();
        f.Add("20-1", "Viejo transporte");
        f.Add("OLD", "Vieja", 11);
        await f.HandAsync(FormCatalogImportFixture.Import(f.Catalog.Id), ProposeImport, 1);

        var preview = await f.Service.PreviewFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(), Ct);

        Assert.True(preview.IsSuccess);
        Assert.Equal((1, 1, 1), (preview.Value.Counts.Added, preview.Value.Counts.Changed, preview.Value.Counts.Deactivated));
        Assert.Equal("OLD", Assert.Single(preview.Value.Deactivated).Code);
        Assert.Equal(1, preview.Value.Confirmations.Deactivations);
        var unconfirmed = await f.Service.ApplyFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(preview.Value.SheetSha256), Ct);
        Assert.Equal(FormCatalogImportErrors.ConfirmationRequired, unconfirmed.Error);
        Assert.True(f.Items.Single(item => item.Code == "OLD").IsActive);

        var applied = await f.Service.ApplyFromAssistantAsync(f.Catalog.Id,
            f.AssistantRequest(preview.Value.SheetSha256) with { ConfirmDeactivations = 1 }, Ct);

        Assert.True(applied.IsSuccess);
        Assert.False(f.Items.Single(item => item.Code == "OLD").IsActive);
        Assert.Equal("Transportes Sur", f.Items.Single(item => item.Code == "20-1").TextEs);
        Assert.Equal(["20-1", "OLD", "20-2"], f.Items.OrderBy(item => item.Position).Select(item => item.Code));
        Assert.Equal(FormCatalogImportFixture.Sha256(f.Copy), f.Catalog.LastImportSha256);
    }

    [Fact]
    public async Task A_replaced_import_proposal_is_a_conflict()
    {
        using var f = new FormCatalogImportFixture();
        await f.HandAsync(FormCatalogImportFixture.Import(f.Catalog.Id), ProposeImport, 1, current: false);

        var preview = await f.Service.PreviewFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(), Ct);
        var applied = await f.Service.ApplyFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(FormCatalogImportFixture.Sha256(f.Copy)), Ct);

        Assert.Equal((FormCatalogErrors.ProposalReplaced, FormCatalogErrors.ProposalReplaced), (preview.Error, applied.Error));
        Assert.Equal((0, 0), (f.CopyReads, f.Work.Transactions));
    }

    // The proposal a catalog was created from counts even after a later one: the import is what is left to do with it.
    [Fact]
    public async Task The_catalog_created_from_a_proposal_imports_it_even_when_a_later_proposal_replaced_it()
    {
        using var f = new FormCatalogImportFixture();
        f.ProposeCatalog();
        await f.HandAsync(f.Mapped, current: false);

        var preview = await f.Service.PreviewFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(), Ct);

        Assert.True(preview.IsSuccess);
        Assert.Equal(2, preview.Value.Counts.Added);
    }

    [Theory]
    [InlineData("another-catalog")]
    [InlineData("not-created-from-it")]
    [InlineData("version-one")]
    [InlineData("without-mapping")]
    [InlineData("without-spreadsheet")]
    [InlineData("form")]
    public async Task A_proposal_without_a_spreadsheet_or_for_another_catalog_imports_nothing(string kind)
    {
        using var f = new FormCatalogImportFixture();
        if (kind is not ("another-catalog" or "not-created-from-it")) f.ProposeCatalog();
        var copy = kind switch
        {
            "another-catalog" => await f.HandAsync(FormCatalogImportFixture.Import(Guid.NewGuid()), ProposeImport, 1),
            "version-one" => await f.HandAsync(f.Mapped.ToCatalog(), version: 1),
            "without-mapping" => await f.HandAsync(f.Mapped with { Spreadsheet = null }),
            "form" => await f.HandAsync(f.Mapped, BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, 1),
            _ => await f.HandAsync(f.Mapped),
        };
        if (kind == "without-spreadsheet") f.Lookup.Response = f.Lookup.Response! with { Attachment = null };

        var preview = await f.Service.PreviewFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(), Ct);
        var applied = await f.Service.ApplyFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(copy.Sha256), Ct);

        Assert.Equal((FormCatalogErrors.ProposalUnavailable, FormCatalogErrors.ProposalUnavailable), (preview.Error, applied.Error));
        Assert.Equal((0, 0, 0), (f.Files.ReadCount, f.CopyReads, f.Work.Transactions));
    }

    // The dialog asks for the file again: the copy the assistant saw is gone, or what is stored is not that file.
    [Theory]
    [InlineData("missing")]
    [InlineData("another-file")]
    [InlineData("longer")]
    [InlineData("shorter")]
    [InlineData("no-storage")]
    public async Task A_copy_that_is_gone_or_is_another_file_asks_for_the_file_again(string kind)
    {
        using var f = new FormCatalogImportFixture(storage: kind != "no-storage");
        f.ProposeCatalog();
        var copy = await f.HandAsync(f.Mapped, store: kind != "missing");
        byte[]? stored = kind switch { "another-file" => [80, 75, 3, 4, 21], "longer" => [.. f.Copy, 0], "shorter" => f.Copy[..^1], _ => null };
        if (stored is not null)
        {
            using var content = new MemoryStream(stored, false);
            await f.Files.WriteAsync(copy.StorageKey, content, copy.ContentType, Ct);
        }

        var preview = await f.Service.PreviewFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(), Ct);
        var applied = await f.Service.ApplyFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(copy.Sha256), Ct);

        Assert.Equal((FormCatalogImportErrors.SheetUnavailable, FormCatalogImportErrors.SheetUnavailable), (preview.Error, applied.Error));
        Assert.Equal((0, 0), (f.CopyReads, f.Work.Transactions));
        Assert.Empty(f.Items);
    }

    [Fact]
    public async Task Applying_needs_the_sheet_that_was_reviewed()
    {
        using var f = new FormCatalogImportFixture();
        f.ProposeCatalog();
        await f.HandAsync(f.Mapped);

        var missing = await f.Service.ApplyFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(), Ct);
        var another = await f.Service.ApplyFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(new('b', 64)), Ct);

        Assert.Contains("sheetSha256", Assert.IsType<ValidationError>(missing.Error).Errors.Keys);
        Assert.Equal(FormCatalogImportErrors.SheetChanged, another.Error);
        Assert.Equal((1, 0), (f.CopyReads, f.Work.Transactions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revoked_permissions_while_reading_the_copy_discard_the_whole_result(bool apply)
    {
        using var f = new FormCatalogImportFixture();
        f.ProposeCatalog();
        var copy = await f.HandAsync(f.Mapped);
        f.WhileReadingFile = f.Revoke;

        Result result = apply ? await f.Service.ApplyFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(copy.Sha256), Ct)
            : await f.Service.PreviewFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(), Ct);

        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Empty(f.Items);
        Assert.Equal(0, f.Work.Commits);
    }

    [Theory]
    [InlineData("missing", "Forms.Catalog.NotFound")]
    [InlineData("stale", "Forms.Catalog.RevisionConflict")]
    [InlineData("reader", "Forms.Definition.Forbidden")]
    public async Task The_catalog_guards_run_before_reading_the_proposal(string state, string code)
    {
        using var f = new FormCatalogImportFixture();
        f.ProposeCatalog();
        var copy = await f.HandAsync(f.Mapped);
        var request = f.AssistantRequest();
        if (state == "missing") f.Exists = false;
        if (state == "stale") f.ChangeRevision();
        if (state == "reader") f.Revoke();

        var preview = await f.Service.PreviewFromAssistantAsync(f.Catalog.Id, request, Ct);
        var applied = await f.Service.ApplyFromAssistantAsync(f.Catalog.Id, request with { SheetSha256 = copy.Sha256 }, Ct);

        Assert.Equal((code, code), (preview.Error.Code, applied.Error.Code));
        Assert.Equal((0, 0), (f.Lookup.Reads, f.Work.Transactions));
    }

    [Fact]
    public async Task The_pending_import_is_for_the_person_who_asked_while_the_catalog_is_pending_and_empty()
    {
        using var f = new FormCatalogImportFixture();
        f.ProposeCatalog();
        await f.HandAsync(f.Mapped);

        var pending = await f.Service.GetAssistantImportAsync(f.Catalog.Id, Ct);

        Assert.Equal(new FormCatalogAssistantImportResponse(f.Catalog.Id, new(f.Lookup.RequestId, f.ProposalId, "transportistas.xlsx", 2)), pending.Value);
        Assert.Equal((0, 0), (f.Work.Transactions, f.CopyReads));
    }

    // The pending import only needs the proposal: the review opens the copy and says when it is gone, so asking for it never reads it.
    [Fact]
    public async Task The_pending_import_does_not_open_the_copy_and_the_review_finds_it_gone()
    {
        using var f = new FormCatalogImportFixture();
        f.ProposeCatalog();
        await f.HandAsync(f.Mapped, store: false);

        var pending = await f.Service.GetAssistantImportAsync(f.Catalog.Id, Ct);

        Assert.NotNull(pending.Value.Pending);
        Assert.Equal(0, f.Files.ReadCount);
        Assert.Equal(FormCatalogImportErrors.SheetUnavailable, (await f.Service.PreviewFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(), Ct)).Error);
    }

    [Theory]
    [InlineData("another-person")]
    [InlineData("without-manage")]
    [InlineData("with-options")]
    [InlineData("approved")]
    [InlineData("from-a-channel")]
    [InlineData("version-one")]
    [InlineData("not-proposed")]
    public async Task Without_an_import_pending_it_answers_without_one(string kind)
    {
        using var f = new FormCatalogImportFixture();
        if (kind != "not-proposed") f.ProposeCatalog(fromHome: kind != "from-a-channel");
        await (kind == "version-one" ? f.HandAsync(f.Mapped.ToCatalog(), version: 1) : f.HandAsync(f.Mapped));
        if (kind == "another-person")
        {
            f.Current.UserId = Guid.NewGuid();
            f.Permissions.Permissions[f.Current.UserId.Value] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        }
        // Reading catalogs is enough to ask, but only who can import has an import pending, as the lookup that checks the turn's
        // reserved permissions would say.
        if (kind == "without-manage") f.Revoke();
        if (kind == "with-options") f.Add("A", "Uno");
        if (kind == "approved") Assert.True(f.Catalog.ApproveManualProposal(f.Catalog.Revision, f.Actor, f.Clock.GetUtcNow().UtcDateTime).IsSuccess);

        var result = await f.Service.GetAssistantImportAsync(f.Catalog.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(new FormCatalogAssistantImportResponse(f.Catalog.Id, null), result.Value);
        if (kind != "version-one") Assert.Equal(0, f.Lookup.Reads);
        Assert.Equal(0, f.Files.ReadCount);
    }

    // As when uploading the file: the column a proposal mapped to a field that is no longer active goes with that field's header, and
    // the review warns it as a column that is not the catalog's instead of dropping it.
    [Fact]
    public async Task A_column_mapped_to_a_field_that_is_no_longer_active_is_warned_in_the_review()
    {
        using var f = new FormCatalogImportFixture();
        f.ProposeCatalog();
        var fields = JsonSerializer.Deserialize<BusinessFormCatalogField[]>(f.Catalog.FieldsJson!, JsonSerializerOptions.Web)!;
        Assert.True(f.Catalog.SetFields([.. fields.Select(field => field with { IsActive = false })], null).IsSuccess);
        await f.HandAsync(f.Mapped);

        var preview = await f.Service.PreviewFromAssistantAsync(f.Catalog.Id, f.AssistantRequest(), Ct);

        Assert.True(preview.IsSuccess);
        var warning = Assert.Single(preview.Value.Issues);
        Assert.Equal(("telefono", "Warning", FormCatalogImportErrors.UnknownColumn.Code), (warning.Column, warning.Severity, warning.Code));
        Assert.Equal(2, preview.Value.Counts.Added);
        Assert.All(preview.Value.Added, row => Assert.Null(row.Fields));
    }

    [Fact]
    public async Task Asking_for_the_pending_import_needs_the_catalog_and_the_read_permission()
    {
        using var f = new FormCatalogImportFixture();
        f.ProposeCatalog();
        await f.HandAsync(f.Mapped);
        f.Exists = false;
        Assert.Equal(FormCatalogErrors.NotFound, (await f.Service.GetAssistantImportAsync(f.Catalog.Id, Ct)).Error);
        f.Exists = true;
        f.Permissions.Permissions[f.Actor] = [];
        Assert.Equal(FormDefinitionErrors.Forbidden, (await f.Service.GetAssistantImportAsync(f.Catalog.Id, Ct)).Error);
    }

    [Theory]
    [InlineData("request", "requestId")]
    [InlineData("empty-request", "requestId")]
    [InlineData("proposal", "proposalId")]
    [InlineData("revision", "expectedRevision")]
    [InlineData("hash", "sheetSha256")]
    [InlineData("confirmation", "confirmDeactivations")]
    public void Invalid_request_fields_are_rejected_before_reading_anything(string kind, string path)
    {
        var request = new FormCatalogAssistantImportRequest(Guid.NewGuid(), Guid.NewGuid(), 1, new('a', 64));
        request = kind switch
        {
            "request" => request with { RequestId = null },
            "empty-request" => request with { RequestId = Guid.Empty },
            "proposal" => request with { ProposalId = null },
            "revision" => request with { ExpectedRevision = null },
            "hash" => request with { SheetSha256 = new('A', 64) },
            _ => request with { ConfirmDeactivations = -1 },
        };

        var result = new FormCatalogAssistantImportRequestValidator().Validate(request);

        Assert.Contains(result.Errors, error => error.PropertyName == path);
        Assert.True(new FormCatalogAssistantImportRequestValidator().Validate(request with
        {
            RequestId = Guid.NewGuid(), ProposalId = Guid.NewGuid(), ExpectedRevision = 1, SheetSha256 = null, ConfirmDeactivations = null,
        }).IsValid);
    }
}
