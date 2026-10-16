using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormPdfDocumentRecorderTests
{
    [Fact]
    public async Task Confirming_the_same_fixed_copies_twice_does_not_duplicate_files_or_documents()
    {
        var documents = new MemoryFormDocumentRepository();
        var files = new MemoryStoredFileRepository();
        var recorder = new FormPdfDocumentRecorder(documents, files);
        var snapshot = Snapshot();
        var outcome = Completed();
        await recorder.AddAsync(snapshot, outcome, CancellationToken.None);
        var ids = documents.Documents.Select(document => document.Id).ToArray();
        await recorder.AddAsync(snapshot, outcome, CancellationToken.None);
        Assert.Equal(ids, documents.Documents.Select(document => document.Id));
        Assert.Equal(2, files.Files.Count);
        Assert.All(files.Files, file => Assert.Equal(snapshot.Record.Response.Actor.UserId, file.OwnerUserId));
        Assert.All(documents.Documents, document => Assert.Equal(snapshot.DesignRevision, document.DesignRevision));
    }

    [Theory]
    [InlineData(FormPdfActionState.Uncertain)]
    [InlineData(FormPdfActionState.NotFound)]
    [InlineData(FormPdfActionState.Failed)]
    public async Task An_unconfirmed_effect_never_creates_fixed_copy_metadata(FormPdfActionState state)
    {
        var documents = new MemoryFormDocumentRepository();
        var files = new MemoryStoredFileRepository();
        var recorder = new FormPdfDocumentRecorder(documents, files);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.AddAsync(Snapshot(), Completed() with { State = state }, CancellationToken.None));
        Assert.Empty(files.Files);
        Assert.Empty(documents.Documents);
    }

    [Fact]
    public async Task A_missing_language_is_rejected_before_adding_any_metadata()
    {
        var documents = new MemoryFormDocumentRepository();
        var files = new MemoryStoredFileRepository();
        var recorder = new FormPdfDocumentRecorder(documents, files);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.AddAsync(Snapshot(), Completed() with { Artifacts = [Completed().Artifacts[0]] }, CancellationToken.None));
        Assert.Empty(files.Files);
        Assert.Empty(documents.Documents);
    }

    private static FormPdfActionSnapshotResponse Snapshot()
    {
        var reader = new FakeFormResponseReader();
        reader.ArrangeExport(1);
        return new(Guid.NewGuid(), Guid.NewGuid(), 3, new(new Dictionary<string, string> { ["es"] = "Visita", ["en"] = "Visit" }, [], true, null, true),
            new(reader.ExportRows[0], "es"), ["es", "en"]);
    }
    private static FormPdfActionOutcomeResponse Completed() => new(FormPdfActionState.Completed,
        [new("es", "fixed/es", "Visita.pdf", "application/pdf", 2, new string('a', 64)),
         new("en", "fixed/en", "Visit.pdf", "application/pdf", 2, new string('b', 64))]);
}
