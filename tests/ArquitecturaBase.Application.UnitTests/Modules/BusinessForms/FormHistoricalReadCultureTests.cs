using System.Globalization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using Microsoft.Extensions.Time.Testing;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormHistoricalReadCultureTests
{
    [Theory]
    [InlineData("es", "en", "sheet")]
    [InlineData("en", "es", "sheet")]
    [InlineData("es", "en", "detail")]
    [InlineData("en", "es", "detail")]
    public async Task A_partially_translated_version_is_read_entirely_in_its_original_language(string original, string ui, string operation)
    {
        var reader = Arrange(original, ui);
        var current = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[current.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var service = new FormResponseService(reader, new(current, permissions),
            RequestValidators.For(new ListFormRecordsRequestValidator()),
            NullLogger<FormResponseService>.Instance, new([]), new FormSuggestionFixture());
        var before = FormDraftPolicy.Serialize(reader.ExportRows[0].Record);
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(ui);
        try
        {
            if (operation == "sheet")
            {
                var result = await service.ListAsync(reader.Definition!.Id, new(), TestContext.Current.CancellationToken);
                Assert.True(result.IsSuccess);
                Assert.Equal("Original form", result.Value.Name);
                Assert.Equal(new[] { "Original choice", "Original notes" }, result.Value.Columns.Select(column => column.Label));
                Assert.Equal("Original option", Assert.IsType<StructuredRecordChoiceValue>(Assert.Single(result.Value.Records.Items).Cells[0].Value).Label);
            }
            else
            {
                var result = await service.GetAsync(reader.ExportRows[0].Record.RecordId, TestContext.Current.CancellationToken);
                Assert.True(result.IsSuccess);
                Assert.Equal("Original form", result.Value.Name);
                Assert.Equal(new[] { "Translated form", "Original form" }, result.Value.Chain.Select(record => record.Name));
                Assert.Equal(new[] { "Original choice", "Original notes" }, result.Value.Values.Select(value => value.Label));
                Assert.Equal("Original option", Assert.IsType<StructuredRecordChoiceValue>(result.Value.Values[0].Value).Label);
            }
            Assert.Equal(ui, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
            Assert.Equal(before, FormDraftPolicy.Serialize(reader.ExportRows[0].Record));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("es", "en", false)]
    [InlineData("en", "es", false)]
    [InlineData("es", "en", true)]
    [InlineData("en", "es", true)]
    public async Task Published_cultures_control_the_whole_read_even_when_every_visible_text_is_translated(string original, string ui, bool published)
    {
        var reader = Arrange(original, ui);
        var version = reader.Definition!.Versions[0];
        var schema = FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson);
        schema = schema with { Questions = schema.Questions.Select(question => question with
            { Label = question.Label.ToDictionary(pair => pair.Key, pair => pair.Value).Concat(
                question.Label.ContainsKey(ui) ? [] : new[] { new KeyValuePair<string, string>(ui, "Translated notes") }).ToDictionary() }).ToArray() };
        version = version with { PublishedCultures = published ? [original, ui] : [original], SchemaJson = FormDraftPolicy.Serialize(schema) };
        var row = reader.ExportRows[0] with { Version = version, Record = reader.ExportRows[0].Record with { SchemaJson = version.SchemaJson } };
        var batch = new FakeFormRecordReadBatch { Batch = new([row.Record], new Dictionary<Guid, FormResponseVersionRow> { [version.Id] = version }) };
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [BusinessFormsPermissions.RecordsRead];
        var source = new BusinessFormRecordSource(batch, permissions);
        var result = await source.ReadAsync(actor, ui, [row.Record.SourceReceiptId], TestContext.Current.CancellationToken);
        var reply = Assert.Single(result.Replies).Value;
        var prefix = published ? "Translated " : "Original ";
        Assert.Equal(prefix + "form", reply.Name);
        Assert.Equal(new[] { prefix + "choice", prefix + "notes" }, reply.Values.Select(value => value.Label));
        Assert.Equal(prefix + "option", Assert.IsType<StructuredRecordChoiceValue>(reply.Values[0].Value).Label);
        Assert.Equal(1, batch.Reads);
        Assert.Equal(FormDraftPolicy.Serialize(row with { Version = null }), FormDraftPolicy.Serialize(row));
    }

    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task Excel_keeps_its_UI_headers_while_version_content_uses_the_published_original(string original, string ui)
    {
        var reader = Arrange(original, ui);
        var current = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[current.UserId.Value] = [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport];
        var writer = new FakeFormWorkbookWriter();
        var service = new FormExportService(reader, writer, new(current, permissions),
            RequestValidators.For(new FormExportRequestValidator(), new ListFormRecordsRequestValidator()),
            new FakeTimeProvider(), NullLogger<FormExportService>.Instance, new([]));
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(ui);
        try
        {
            Assert.True((await service.ExportAsync(reader.Definition!.Id, new(new(), null), TestContext.Current.CancellationToken)).IsSuccess);
            Assert.Equal(ui == "en" ? "Number" : "Número", writer.Request!.Headers[0]);
            Assert.Equal("Original choice", writer.Request.Headers[4]);
            Assert.Equal("Original option", writer.Request.Rows[0][4].Value);
            Assert.Equal(ui, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    private static FakeFormResponseReader Arrange(string original, string ui)
    {
        var reader = new FakeFormResponseReader();
        reader.ArrangeExport(1);
        IReadOnlyDictionary<string, string> Both(string text) => new Dictionary<string, string> { [original] = "Original " + text, [ui] = "Translated " + text };
        var field = Guid.NewGuid();
        var notes = Guid.NewGuid();
        var option = Guid.NewGuid();
        var schema = new FormVersionSchema(1, [
            new(field, "field_" + field.ToString("N"), "choice", Both("choice"), null, true, null, [new(option, Both("option"))]),
            new(notes, "field_" + notes.ToString("N"), "shortText", new Dictionary<string, string> { [original] = "Original notes" }, null, false, 80, null)
        ]);
        var version = reader.Definition!.Versions[0] with { OriginalCulture = original, PublishedCultures = [original], NameJson = FormDraftPolicy.Serialize(Both("form")), SchemaJson = FormDraftPolicy.Serialize(schema) };
        reader.Definition = new(reader.Definition.Id, reader.Definition.Prefix, version.Id, [version]);
        var saved = reader.ExportRows[0];
        reader.ExportRows = [saved with { Version = version, Record = saved.Record with { NameJson = version.NameJson, SchemaJson = version.SchemaJson,
            ValuesJson = FormDraftPolicy.Serialize(new[] { new { fieldId = field, value = option } }) } }];
        var priorVersion = version with { Id = Guid.NewGuid(), PublishedCultures = [original, ui] };
        reader.Chain = [
            new(Guid.NewGuid(), reader.Definition.Id, null, reader.Definition.Prefix, 1, version.NameJson, saved.SubmittedAtUtc.AddMinutes(-1)) { Version = priorVersion },
            new(saved.Record.RecordId, reader.Definition.Id, null, reader.Definition.Prefix, 2, version.NameJson, saved.SubmittedAtUtc) { Version = version }];
        return reader;
    }
}
