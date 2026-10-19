using System.Globalization;
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

public sealed class FormResponseLanguageTests
{
    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public void A_monolingual_record_keeps_its_historical_name_labels_and_choice_in_the_other_ui_language(string original, string secondary)
    {
        var reader = Reader(original);
        var row = reader.ExportRows[0].Record;
        var unchanged = FormDraftPolicy.Serialize(row);

        var reply = FormRecordPresentationPolicy.Project(row, secondary, reader.Definition!.Schemas[row.VersionId]);

        Assert.Equal("Original form", reply.Name);
        Assert.Equal(new[] { "Original choice", "Original boolean", "Original notes" }, reply.Values.Select(value => value.Label));
        Assert.Equal("Original option", Assert.IsType<StructuredRecordChoiceValue>(reply.Values[0].Value).Label);
        Assert.False(Assert.IsType<bool>(reply.Values[1].Value));
        Assert.Null(reply.Values[2].Value);
        Assert.Equal(unchanged, FormDraftPolicy.Serialize(row));
    }

    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public void Monolingual_sheet_columns_and_filter_choices_use_their_saved_original_texts(string original, string secondary)
    {
        var reader = Reader(original);

        var columns = FormResponsePresentationPolicy.Columns(reader.Definition!, secondary);

        Assert.Equal(new[] { "Original choice", "Original boolean", "Original notes" }, columns.Select(column => column.Label));
        Assert.Equal("Original option", Assert.Single(columns[0].Options).Label);
        Assert.Equal(reader.Definition!.Schemas[reader.Definition.ActiveVersionId].Questions.Select(question => question.FieldId),
            columns.Select(column => column.FieldId));
    }

    [Theory]
    [InlineData("es", "en", "sheet")]
    [InlineData("en", "es", "sheet")]
    [InlineData("es", "en", "detail")]
    [InlineData("en", "es", "detail")]
    public async Task Monolingual_response_reads_keep_original_content_while_the_ui_stays_in_the_other_language(
        string original, string secondary, string operation)
    {
        var reader = Reader(original);
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var service = new FormResponseService(reader, new(user, permissions),
            RequestValidators.For(new FormNavigationRequestValidator(), new ListFormRecordsRequestValidator()),
            NullLogger<FormResponseService>.Instance, new([]), new FormSuggestionFixture());
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(secondary);
        try
        {
            if (operation == "sheet")
            {
                var result = await service.ListAsync(reader.Definition!.Id, new(), TestContext.Current.CancellationToken);
                Assert.True(result.IsSuccess);
                Assert.Equal("Original form", result.Value.Name);
                Assert.Equal("Original choice", result.Value.Columns[0].Label);
                var cells = Assert.Single(result.Value.Records.Items).Cells;
                Assert.Equal("Original option", Assert.IsType<StructuredRecordChoiceValue>(cells[0].Value).Label);
                Assert.Equal(false, cells[1].Value);
                Assert.Equal("Unanswered", cells[2].State);
            }
            else
            {
                var result = await service.GetAsync(reader.ExportRows[0].Record.RecordId, TestContext.Current.CancellationToken);
                Assert.True(result.IsSuccess);
                Assert.Equal("Original form", result.Value.Name);
                Assert.Equal("Original choice", result.Value.Values[0].Label);
                Assert.Equal("Original option", Assert.IsType<StructuredRecordChoiceValue>(result.Value.Values[0].Value).Label);
                Assert.Equal(new[] { "Original form", "Original form" }, result.Value.Chain.Select(record => record.Name));
            }
            Assert.Equal(secondary, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private static FakeFormResponseReader Reader(string original)
    {
        IReadOnlyDictionary<string, string> Text(string value) => new Dictionary<string, string> { [original] = value };
        var reader = new FakeFormResponseReader();
        reader.ArrangeExport(1);
        var choiceId = Guid.NewGuid();
        var booleanId = Guid.NewGuid();
        var notesId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        var schema = new FormVersionSchema(1, [
            new(choiceId, "field_" + choiceId.ToString("N"), "choice", Text("Original choice"), null, true, null,
                [new(optionId, Text("Original option"))]),
            new(booleanId, "field_" + booleanId.ToString("N"), "boolean", Text("Original boolean"), null, true, null, null),
            new(notesId, "field_" + notesId.ToString("N"), "shortText", Text("Original notes"), null, false, 80, null)
        ]);
        var version = reader.Definition!.Versions[0] with
        {
            NameJson = FormDraftPolicy.Serialize(Text("Original form")),
            SchemaJson = FormDraftPolicy.Serialize(schema)
        };
        reader.Definition = new(reader.Definition.Id, reader.Definition.Prefix, version.Id, [version]);
        var saved = reader.ExportRows[0];
        reader.ExportRows = [saved with
        {
            Record = saved.Record with
            {
                NameJson = version.NameJson, SchemaJson = version.SchemaJson,
                ValuesJson = FormDraftPolicy.Serialize(new object[] {
                    new { fieldId = choiceId, value = optionId }, new { fieldId = booleanId, value = false }
                })
            }
        }];
        reader.Chain = [
            new(Guid.NewGuid(), reader.Definition.Id, null, "PREV", 1, version.NameJson, saved.SubmittedAtUtc.AddMinutes(-1)),
            new(saved.Record.RecordId, reader.Definition.Id, null, "T", 1, version.NameJson, saved.SubmittedAtUtc)
        ];
        return reader;
    }
}
