using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Files;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Files;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task A_pdf_uploaded_before_a_crash_survives_cleanup_without_metadata_and_reconciles_its_original_bytes()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var snapshot = await SeedUnconfirmedPdfRunAsync(actor); var key = FormPdfStorageKey.Create(snapshot.RunId, "es");
        await using var emulator = new AzuriteFixture(); await emulator.StartAsync(Ct);
        var store = new AzureBlobFileStore(Options.Create(new FileStorageOptions { Enabled = true, CredentialMode = "ConnectionString", ConnectionString = emulator.ConnectionString }));
        var bytes = "%PDF-1.7\n1 0 obj<<>>endobj\n%%EOF"u8.ToArray();
        await using (var content = new MemoryStream(bytes)) Assert.True(await store.WriteOnceAsync(key, content, "application/pdf", Ct));
        await using (var orphan = new MemoryStream([1])) await store.WriteAsync("orphan-control", orphan, "application/octet-stream", Ct);
        var blob = new BlobServiceClient(emulator.ConnectionString).GetBlobContainerClient("files-content").GetBlobClient(key);
        var clock = new FakeTimeProvider((await blob.GetPropertiesAsync(cancellationToken: Ct)).Value.LastModified.AddDays(2));
        await using var recovered = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPrivateFileStore>(); services.AddSingleton<IPrivateFileStore>(store);
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
        }));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Set<StoredFile>().AnyAsync(file => file.StorageKey == key, Ct)));
        var purged = await InActionScopeAsync(recovered.Services, scope => scope.GetRequiredService<IPrivateFileCleanupService>().PurgeAsync(Ct));
        Assert.True(purged.IsSuccess); Assert.Null(await store.OpenReadAsync("orphan-control", Ct));
        await using (var saved = await store.OpenReadAsync(key, Ct))
        {
            Assert.NotNull(saved); using var content = new MemoryStream(); await saved.CopyToAsync(content, Ct); Assert.Equal(bytes, content.ToArray());
        }
        var work = (await InActionScopeAsync(recovered.Services, scope => scope.GetRequiredService<IFormActionDispatchService>().ReserveAsync(snapshot.RunId, Ct))).Value;
        Assert.NotNull(work); Assert.True(work.ReconciliationOnly);
        var outcome = await InActionScopeAsync(recovered.Services, scope => scope.GetRequiredService<IFormActionEffectService>().ExecuteAsync(work, Ct));
        Assert.Equal(FormActionRunState.Completed, outcome.State);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), Assert.Single(outcome.Pdf!.Artifacts).Sha256);
        Assert.True((await InActionScopeAsync(recovered.Services, scope => scope.GetRequiredService<IFormActionDispatchService>().CompleteAsync(work, outcome, Ct))).IsSuccess);
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(1, await db.Set<BusinessFormDocument>().CountAsync(document => document.RunId == snapshot.RunId, Ct));
            Assert.Equal(1, await db.Set<StoredFile>().CountAsync(file => file.StorageKey == key, Ct));
        });
    }
    [Fact]
    public async Task An_unconfirmed_pdf_becomes_an_orphan_after_its_response_and_execution_are_removed()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.RecordsRead]); var snapshot = await SeedUnconfirmedPdfRunAsync(actor);
        var key = FormPdfStorageKey.Create(snapshot.RunId, "es");
        await using var emulator = new AzuriteFixture(); await emulator.StartAsync(Ct);
        var store = new AzureBlobFileStore(Options.Create(new FileStorageOptions { Enabled = true, CredentialMode = "ConnectionString", ConnectionString = emulator.ConnectionString }));
        await using (var bytes = new MemoryStream([1])) Assert.True(await store.WriteOnceAsync(key, bytes, "application/pdf", Ct));
        var blob = new BlobServiceClient(emulator.ConnectionString).GetBlobContainerClient("files-content").GetBlobClient(key);
        var clock = new FakeTimeProvider((await blob.GetPropertiesAsync(cancellationToken: Ct)).Value.LastModified.AddDays(2));
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormActionRun>().Remove(await db.Set<BusinessFormActionRun>().SingleAsync(run => run.Id == snapshot.RunId, Ct));
            await db.SaveChangesAsync(Ct);
            db.Set<BusinessFormRecord>().Remove(await db.Set<BusinessFormRecord>().SingleAsync(record => record.Id == snapshot.Record.Response.Record.RecordId, Ct));
            await db.SaveChangesAsync(Ct);
        });
        await using var removed = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPrivateFileStore>(); services.AddSingleton<IPrivateFileStore>(store);
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
        }));
        Assert.True((await InActionScopeAsync(removed.Services, scope => scope.GetRequiredService<IPrivateFileCleanupService>().PurgeAsync(Ct))).IsSuccess);
        Assert.Null(await store.OpenReadAsync(key, Ct));
    }
    private async Task<FormPdfActionSnapshotResponse> SeedUnconfirmedPdfRunAsync(HttpClient actor)
    {
        var sheet = await SeedSheetAsync(actor, 0); var author = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        return await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(item => item.Id == sheet.Id, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(item => item.Id == definition.ActiveVersionId, Ct);
            var action = new FormRuleAction(Guid.NewGuid(), "GeneratePdf", null, null, false);
            var rule = new FormRule(Guid.NewGuid(), "Approved", [], [action]);
            var ruleSet = BusinessFormRuleSet.Create(definition.Id, 1, "es", FormDraftPolicy.Serialize(new[] { rule }), false, version.SchemaJson);
            db.Set<BusinessFormRuleSet>().Add(ruleSet); await db.SaveChangesAsync(Ct);
            var now = factory.Clock.GetUtcNow().UtcDateTime;
            var record = BusinessFormRecord.Create(definition.Id, version.Id, definition.AllocateRecordNumber(), author, Guid.NewGuid(), now, "[]", ruleSet.Id);
            Assert.True(record.MarkPendingApproval().IsSuccess); Assert.True(record.CompleteApproval(true).IsSuccess); db.Set<BusinessFormRecord>().Add(record);
            var row = new FormRecordStoredRow(record.SourceReceiptId, record.Id, record.Number, definition.Id, definition.Prefix, version.Id, version.Number, version.NameJson!, version.SchemaJson, record.ValuesJson);
            var data = new FormActionRecordSnapshot(new(row, now, new(author, "Author")), "es", new(Guid.NewGuid(), Guid.NewGuid(), "Supervisor", now, "Approved", null));
            var run = BusinessFormActionRun.Create(definition.Id, record.Id, ruleSet.Id, rule.Id, action.Id, "Approved", "GeneratePdf", 0, null, "", true, now);
            var snapshot = new FormPdfActionSnapshotResponse(run.Id, null, 0, new(new Dictionary<string,string> { ["es"] = "Visita" }, [], true, null, true), data, ["es"]);
            run.SealSnapshot(FormDraftPolicy.Serialize(new FormActionSnapshot(data, action, FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson), snapshot, null, [])));
            Assert.True(run.Reserve(now, TimeSpan.FromMinutes(2)).IsSuccess); db.Set<BusinessFormActionRun>().Add(run);
            await db.SaveChangesAsync(Ct); return snapshot;
        });
    }
}
