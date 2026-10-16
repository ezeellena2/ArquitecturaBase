using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormPdfDocumentServiceTests
{
    [Fact]
    public async Task A_caller_without_record_read_cannot_lookup_or_open_a_private_document()
    {
        var rules = new FormRuleFixture(); var reader = new FormPdfDocumentReaderDouble(); var store = new MemoryPrivateFileStore();
        var result = await Service(rules, reader, store).GetAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error); Assert.Equal(0, reader.Reads); Assert.Equal(0, store.ReadCount);
    }
    [Fact]
    public async Task A_document_from_another_response_is_not_found_before_any_blob_is_opened()
    {
        var rules = Allowed(); var reader = Reader(); var store = new MemoryPrivateFileStore();
        var result = await Service(rules, reader, store).GetAsync(Guid.NewGuid(), reader.Document!.Id, CancellationToken.None);
        Assert.Equal(FormPdfErrors.NotFound, result.Error); Assert.Equal(0, store.ReadCount);
    }
    [Fact]
    public async Task Missing_private_content_is_reported_as_not_found()
    {
        var rules = Allowed(); var reader = Reader(); var document = reader.Document!; var store = new MemoryPrivateFileStore();
        var result = await Service(rules, reader, store).GetAsync(document.RecordId, document.Id, CancellationToken.None);
        Assert.Equal(FormPdfErrors.NotFound, result.Error); Assert.Equal(1, store.ReadCount);
    }
    [Fact]
    public async Task Revoking_record_read_while_opening_content_closes_the_stream_without_returning_it()
    {
        var rules = Allowed(); var reader = Reader(); var document = reader.Document!; var store = new MemoryPrivateFileStore();
        await using var content = new MemoryStream([1, 2, 3]); await store.WriteAsync(document.StorageKey, content, "application/pdf", CancellationToken.None);
        store.BeforeRead = () => rules.Permissions.Permissions[rules.User.UserId!.Value] = [];
        var result = await Service(rules, reader, store).GetAsync(document.RecordId, document.Id, CancellationToken.None);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error); Assert.NotNull(store.LastOpenedStream); Assert.False(store.LastOpenedStream.CanRead);
    }
    [Fact]
    public async Task Record_read_alone_returns_the_fixed_document_and_its_original_culture()
    {
        var rules = Allowed(); var reader = Reader(); var document = reader.Document!; var store = new MemoryPrivateFileStore();
        await using var content = new MemoryStream([1, 2, 3]); await store.WriteAsync(document.StorageKey, content, "application/pdf", CancellationToken.None);
        var result = await Service(rules, reader, store).GetAsync(document.RecordId, document.Id, CancellationToken.None);
        Assert.True(result.IsSuccess); Assert.Equal(document.Name, result.Value.Name); Assert.Equal("es", result.Value.Culture);
        await using var stream = result.Value.Content; using var bytes = new MemoryStream(); await stream.CopyToAsync(bytes, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes.ToArray());
    }
    private static FormRuleFixture Allowed()
    {
        var rules = new FormRuleFixture(); rules.Permissions.Permissions[rules.User.UserId!.Value] = [BusinessFormsPermissions.RecordsRead]; return rules;
    }
    private static FormPdfDocumentReaderDouble Reader() => new() { Document = new FormPdfDocumentRow(Guid.NewGuid(), Guid.NewGuid(), "es", "Constancia.pdf", "private/fixed-document") };
    private static FormPdfDocumentService Service(FormRuleFixture rules, FormPdfDocumentReaderDouble reader, MemoryPrivateFileStore store) =>
        new(reader, store, new(rules.User, rules.Permissions), NullLogger<FormPdfDocumentService>.Instance);
}
