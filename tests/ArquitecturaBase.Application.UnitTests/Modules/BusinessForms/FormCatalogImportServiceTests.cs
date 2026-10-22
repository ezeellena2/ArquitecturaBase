using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogImportServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Preview_reads_without_a_transaction_and_apply_rebuilds_inside_one_locked_revision()
    {
        using var f = new FormCatalogImportFixture();
        var old = f.Add();
        var preview = await f.Service.PreviewAsync(f.Catalog.Id, f.Request(), Ct);
        Assert.True(preview.IsSuccess);
        Assert.Equal(1, preview.Value.Counts.Added);
        Assert.Equal(1, f.Catalog.Revision);
        Assert.Equal(1, f.FileReads);
        Assert.Equal(0, f.Locks);
        Assert.Equal(0, f.Work.Transactions);
        Assert.Single(f.Items);

        var applied = await f.Service.ApplyAsync(f.Catalog.Id, f.Request(), Ct);

        Assert.True(applied.IsSuccess);
        Assert.Equal(2, f.FileReads);
        Assert.Equal(1, f.Locks);
        Assert.Equal(1, f.Work.Transactions);
        Assert.Equal(1, f.Work.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, f.Work.LastPolicy);
        Assert.Equal(2, applied.Value.Catalog.Revision);
        Assert.Equal(2, applied.Value.Catalog.ItemsCount);
        Assert.Empty(applied.Value.ItemIdentityMap);
        Assert.Equal(FormCatalogImportFixture.Hash, f.Catalog.LastImportSha256);
        Assert.Equal(f.Clock.GetUtcNow().UtcDateTime, f.Catalog.LastImportAtUtc);
        Assert.Equal(10, old.Position);
        Assert.Equal(11, f.Items.Single(item => item.Code == "NEW").Position);
        Assert.Null(applied.Value.Catalog.UsageCount);
        Assert.Equal(0, f.UsageReads);
        Assert.True(f.Content.CanRead);
    }

    [Theory]
    [InlineData("missing", "Forms.Catalog.NotFound")]
    [InlineData("filters", "Forms.Catalog.ManualItemsUnavailable")]
    [InlineData("stale", "Forms.Catalog.RevisionConflict")]
    [InlineData("denied", "Forms.Definition.Forbidden")]
    public async Task Catalog_guards_run_before_opening_the_file(string state, string errorCode)
    {
        using var f = new FormCatalogImportFixture(filters: state == "filters");
        var request = f.Request();
        if (state == "missing") f.Exists = false;
        if (state == "stale") f.ChangeRevision();
        if (state == "denied") f.Revoke();

        var preview = await f.Service.PreviewAsync(f.Catalog.Id, request, Ct);
        var applied = await f.Service.ApplyAsync(f.Catalog.Id, request, Ct);

        Assert.Equal(errorCode, preview.Error.Code);
        Assert.Equal(errorCode, applied.Error.Code);
        Assert.Equal(0, f.FileReads);
        Assert.Equal(0, f.Work.Transactions);
        Assert.Equal(0, f.Locks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_revision_changed_during_parsing_cannot_be_previewed_or_applied(bool apply)
    {
        using var f = new FormCatalogImportFixture();
        var request = f.Request();
        f.WhileReadingFile = f.ChangeRevision;
        Result result = apply ? await f.Service.ApplyAsync(f.Catalog.Id, request, Ct)
            : await f.Service.PreviewAsync(f.Catalog.Id, request, Ct);
        Assert.Equal(FormCatalogErrors.RevisionConflict, result.Error);
        Assert.Empty(f.Items);
        Assert.Equal(0, f.Work.Commits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revoked_permissions_after_parsing_discard_the_whole_result(bool apply)
    {
        using var f = new FormCatalogImportFixture();
        f.WhileReadingFile = f.Revoke;
        Result result = apply ? await f.Service.ApplyAsync(f.Catalog.Id, f.Request(), Ct)
            : await f.Service.PreviewAsync(f.Catalog.Id, f.Request(), Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Empty(f.Items);
        Assert.Equal(0, f.Work.Commits);
        Assert.Equal(0, f.UsageReads);
    }

    [Fact]
    public async Task Preview_rechecks_the_revision_after_reading_the_complete_item_set()
    {
        using var f = new FormCatalogImportFixture();
        f.BeforeMetadata = count => { if (count == 2) f.ChangeRevision(); };
        var result = await f.Service.PreviewAsync(f.Catalog.Id, f.Request(), Ct);
        Assert.Equal(FormCatalogErrors.RevisionConflict, result.Error);
        Assert.Equal(0, f.Work.Transactions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_rechecks_permissions_and_revision_after_taking_the_catalog_lock(bool revoke)
    {
        using var f = new FormCatalogImportFixture();
        f.WhileLocking = revoke ? f.Revoke : f.ChangeRevision;
        var result = await f.Service.ApplyAsync(f.Catalog.Id, f.Request(), Ct);
        Assert.Equal(revoke ? FormDefinitionErrors.Forbidden : FormCatalogErrors.RevisionConflict, result.Error);
        Assert.Empty(f.Items);
        Assert.Null(f.Catalog.LastImportSha256);
        Assert.Equal(0, f.Work.Commits);
        Assert.Equal(1, f.Work.Rollbacks);
    }

    [Fact]
    public async Task A_different_sheet_hash_is_rejected_before_the_write_boundary()
    {
        using var f = new FormCatalogImportFixture();
        var result = await f.Service.ApplyAsync(f.Catalog.Id, f.Request() with { SheetSha256 = new('b', 64) }, Ct);
        Assert.Equal(FormCatalogImportErrors.SheetChanged, result.Error);
        Assert.Equal(1, f.FileReads);
        Assert.Equal(0, f.Work.Transactions);
        Assert.Empty(f.Items);
    }

    [Fact]
    public async Task A_file_error_is_returned_without_opening_a_transaction()
    {
        using var f = new FormCatalogImportFixture { FileError = FormCatalogImportErrors.FileUnreadable };
        var result = await f.Service.ApplyAsync(f.Catalog.Id, f.Request(), Ct);
        Assert.Equal(FormCatalogImportErrors.FileUnreadable, result.Error);
        Assert.Equal(0, f.Work.Transactions);
    }

    [Fact]
    public async Task Applying_requires_a_hash_even_when_preview_does_not()
    {
        using var f = new FormCatalogImportFixture();
        var result = await f.Service.ApplyAsync(f.Catalog.Id, f.Request() with { SheetSha256 = null }, Ct);
        Assert.Contains("sheetSha256", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        Assert.Equal(0, f.FileReads);
        Assert.Equal(0, f.Work.Transactions);
    }

    [Fact]
    public async Task An_old_preview_cannot_be_applied_twice_and_a_new_preview_reports_the_last_import()
    {
        using var f = new FormCatalogImportFixture();
        var request = f.Request();
        Assert.True((await f.Service.ApplyAsync(f.Catalog.Id, request, Ct)).IsSuccess);
        var second = await f.Service.ApplyAsync(f.Catalog.Id, request, Ct);
        Assert.Equal(FormCatalogErrors.RevisionConflict, second.Error);
        Assert.Equal(1, f.Work.Commits);
        var preview = await f.Service.PreviewAsync(f.Catalog.Id, f.Request() with { SheetSha256 = null }, Ct);
        Assert.True(preview.IsSuccess);
        Assert.Equal(f.Catalog.LastImportAtUtc, preview.Value.AlreadyAppliedAtUtc);
        Assert.Equal(0, preview.Value.Counts.Added);
        Assert.Equal(0, preview.Value.Counts.Changed);
    }

    [Fact]
    public async Task Confirmations_are_checked_before_mutating_items_or_the_import_marker()
    {
        using var f = new FormCatalogImportFixture();
        f.Add();
        f.Sheet = f.Sheet with { Rows = [f.Sheet.Rows[0]] };
        var rejected = await f.Service.ApplyAsync(f.Catalog.Id, f.Request("Replace"), Ct);
        Assert.Equal(FormCatalogImportErrors.ConfirmationRequired, rejected.Error);
        Assert.True(Assert.Single(f.Items).IsActive);
        Assert.Null(f.Catalog.LastImportSha256);
        var applied = await f.Service.ApplyAsync(f.Catalog.Id,
            f.Request("Replace") with { ConfirmEmpty = true, ConfirmDeactivations = 1 }, Ct);
        Assert.True(applied.IsSuccess);
        Assert.False(Assert.Single(f.Items).IsActive);
        Assert.Equal(2, f.Catalog.Revision);
        Assert.Equal(1, f.Work.Commits);
    }

    [Fact]
    public async Task An_invalid_existing_row_is_preserved_while_valid_rows_are_applied()
    {
        using var f = new FormCatalogImportFixture();
        var old = f.Add();
        f.Sheet = f.Sheet with { Rows = [f.Sheet.Rows[0], FormCatalogImportFixture.Row(2, "OLD", ""), f.Sheet.Rows[1] with { Number = 3 }] };
        var result = await f.Service.ApplyAsync(f.Catalog.Id, f.Request("Replace") with { ConfirmErrors = true }, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("Viejo", old.TextEs);
        Assert.Equal(10, old.Position);
        Assert.True(old.IsActive);
        Assert.Equal(2, f.Items.Count);
    }

    [Fact]
    public async Task A_successful_import_returns_actual_usage_only_with_the_form_read_permission()
    {
        using var f = new FormCatalogImportFixture();
        f.Permissions.Permissions[f.Actor] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage, BusinessFormsPermissions.Read];
        var result = await f.Service.ApplyAsync(f.Catalog.Id, f.Request(), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Catalog.UsageCount);
        Assert.Equal(1, f.UsageReads);
    }

    [Theory]
    [InlineData("revision", "expectedRevision")]
    [InlineData("mode", "mode")]
    [InlineData("numeric-mode", "mode")]
    [InlineData("hash", "sheetSha256")]
    [InlineData("uppercase-hash", "sheetSha256")]
    [InlineData("confirmation", "confirmDeactivations")]
    [InlineData("content", "content")]
    [InlineData("filename", "fileName")]
    public void Invalid_request_fields_are_rejected_before_parsing(string kind, string path)
    {
        using var f = new FormCatalogImportFixture();
        var request = f.Request();
        request = kind switch
        {
            "revision" => request with { ExpectedRevision = 0 },
            "mode" => request with { Mode = "append" },
            "numeric-mode" => request with { Mode = "1" },
            "hash" => request with { SheetSha256 = "xyz" },
            "uppercase-hash" => request with { SheetSha256 = new('A', 64) },
            "confirmation" => request with { ConfirmDeactivations = -1 },
            "content" => request with { Content = null! },
            _ => request with { FileName = " " }
        };
        var result = new FormCatalogImportRequestValidator().Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == path);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revocation_during_the_initial_metadata_read_hides_missing_or_stale_catalogs(bool apply)
    {
        using var f = new FormCatalogImportFixture();
        var request = f.Request();
        f.BeforeMetadata = _ =>
        {
            f.Revoke();
            if (apply) f.ChangeRevision();
            else f.Exists = false;
        };
        Result result = apply ? await f.Service.ApplyAsync(f.Catalog.Id, request, Ct)
            : await f.Service.PreviewAsync(f.Catalog.Id, request, Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(0, f.FileReads);
        Assert.Equal(0, f.Work.Transactions);
        Assert.Equal(0, f.Locks);
    }


}
