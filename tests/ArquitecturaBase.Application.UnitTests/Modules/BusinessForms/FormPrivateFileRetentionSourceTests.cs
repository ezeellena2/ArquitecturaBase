using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormPrivateFileRetentionSourceTests
{
    [Fact]
    public async Task Copies_are_referenced_while_their_pdf_execution_and_response_exist_even_without_file_metadata()
    {
        var fixture = new FormActionFixture();
        var captured = fixture.Capture([new(Guid.NewGuid(), "Approved", [], [FormActionFixture.Action("GeneratePdf"), FormActionFixture.Action()])]);
        await fixture.Recorder.RecordAsync(captured.Record, captured.Snapshot, "Approved", CancellationToken.None);
        var run = fixture.Runs.Runs[0];
        var original = FormPdfStorageKey.Create(run.Id, "es"); var alternate = FormPdfStorageKey.Create(run.Id, "en");
        var keys = new[] { original, alternate, FormPdfStorageKey.Create(fixture.Runs.Runs[1].Id, "es"), FormPdfStorageKey.Create(Guid.NewGuid(), "es") };
        var source = new FormPrivateFileRetentionSource(fixture.Runs);
        Assert.Equal(new[] { original, alternate }, await source.ListReferencedKeysAsync(keys, CancellationToken.None));
        fixture.Runs.Runs.Clear(); fixture.Runs.Records.Clear();
        Assert.Empty(await source.ListReferencedKeysAsync(keys, CancellationToken.None));
    }
    [Fact]
    public async Task An_existing_run_does_not_protect_foreign_or_malformed_storage_keys()
    {
        var fixture = new FormActionFixture();
        var captured = fixture.Capture([new(Guid.NewGuid(), "Approved", [], [FormActionFixture.Action("GeneratePdf")])]);
        await fixture.Recorder.RecordAsync(captured.Record, captured.Snapshot, "Approved", CancellationToken.None);
        var id = Assert.Single(fixture.Runs.Runs).Id; var key = FormPdfStorageKey.Create(id, "es");
        var source = new FormPrivateFileRetentionSource(fixture.Runs);
        var keys = new[] { key, "responses/" + id.ToString("N") + "/es.pdf", "form-documents/" + id.ToString("N") + "/fr.pdf", "form-documents/" + id.ToString("D") + "/es.pdf" };
        Assert.Equal(new[] { key }, await source.ListReferencedKeysAsync(keys, CancellationToken.None));
    }
}
