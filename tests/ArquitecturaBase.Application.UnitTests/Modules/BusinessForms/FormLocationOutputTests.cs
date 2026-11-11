using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// UB4-A: the Excel spreads a point over three number columns; a rule message carries the point and, below it, the link to the
// map, because WhatsApp has no text with a link; the preview of the PDF shows an example point.
public sealed class FormLocationOutputTests
{
    private static readonly Guid FieldId = Guid.Parse("01990000-0000-7000-8000-000000000902");

    public static TheoryData<string, string?, object[]> Exports => new()
    {
        { "es", """{"latitude":-34.603722,"longitude":-58.381592,"accuracyMeters":2400}""", [-34.603722m, -58.381592m, 2400L] },
        { "en", """{"latitude":-34.603722,"longitude":-58.381592}""", [-34.603722m, -58.381592m, null!] },
        { "es", null, [null!, null!, null!] },
    };

    [Theory]
    [MemberData(nameof(Exports))]
    public async Task The_excel_spreads_a_point_over_three_number_columns(string culture, string? location, object?[] expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var ports = Create();
            ports.Reader.ArrangeExport(1);
            var initial = ports.Reader.Definition!;
            var version = initial.Versions.Single() with { SchemaJson = JsonSerializer.Serialize(new FormVersionSchema(1, [Field()]), JsonSerializerOptions.Web) };
            ports.Reader.Definition = new(initial.Id, initial.Prefix, version.Id, [version]);
            ports.Reader.ExportRows = ports.Reader.ExportRows.Select(row => row with
            {
                Version = version,
                Record = row.Record with
                {
                    SchemaJson = version.SchemaJson,
                    ValuesJson = location is null ? "[]" : "[{\"fieldId\":\"" + FieldId.ToString("D") + "\",\"value\":" + location + "}]",
                }
            }).ToArray();

            var result = await ports.Service.ExportAsync(initial.Id, new(new(), null), CancellationToken.None);

            Assert.True(result.IsSuccess);
            var sheet = ports.Writer.Request!.Sheets[0];
            Assert.Equal(culture == "es"
                ? new[] { "Dónde estás · Latitud", "Dónde estás · Longitud", "Dónde estás · Precisión (m)" }
                : ["Where you are · Latitude", "Where you are · Longitude", "Where you are · Accuracy (m)"], sheet.Headers.Skip(4));
            var cells = Assert.Single(sheet.Rows).Skip(4).ToArray();
            Assert.Equal(3, cells.Length);
            for (var index = 0; index < 3; index++)
            {
                Assert.Equal(expected[index], cells[index].Value);
                if (expected[index] is not null) Assert.Equal(index == 2 ? "integer" : "decimal", cells[index].Kind);
            }
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void A_version_without_the_question_writes_a_dash_in_the_three_columns()
    {
        var cell = new FormResponseCell(FieldId, "location", "NotAsked", null);

        Assert.All(Enumerable.Range(0, 3), part => Assert.Equal("—", FormExportPolicy.LocationCell(cell, part).Value));
    }

    [Theory]
    [InlineData("es", "Ubicación: {Dónde estás}", "Ubicación: -34.603722, -58.381592\nhttps://www.google.com/maps?q=-34.603722,-58.381592")]
    [InlineData("en", "Location: {Where you are}", "Location: -34.603722, -58.381592\nhttps://www.google.com/maps?q=-34.603722,-58.381592")]
    public void A_rule_message_carries_the_point_and_the_link_to_the_map_below(string culture, string template, string expected)
    {
        var row = Row("""{"latitude":-34.603722,"longitude":-58.381592,"accuracyMeters":15}""");
        var action = new FormRuleAction(Guid.NewGuid(), "NotifyAuthor", null,
            new Dictionary<string, string> { [culture] = template }, false);

        Assert.Equal(expected, FormActionMessagePolicy.Render(action, Schema(), Snapshot(row), culture));
    }

    [Fact]
    public void The_point_and_its_map_link_use_six_decimals_and_a_point_in_any_language()
    {
        var location = new StructuredRecordLocationValue(-34.6m, 58.381592m);

        Assert.Equal("-34.600000, 58.381592", location.Coordinates());
        Assert.Equal("https://www.google.com/maps?q=-34.600000,58.381592", location.MapUrl());
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task The_pdf_preview_shows_an_example_point_without_saving(string culture)
    {
        var fixture = new FormPdfFixture(); var definition = fixture.Rules.Definition;
        Assert.True(definition.SetDraft(definition.Revision, definition.Name, definition.NameJson, definition.Prefix,
            definition.Culture, definition.SubmitLabelJson, FormDraftPolicy.Serialize(new FormQuestionSchema(1, [Field() with { Required = true }])), 1, true).IsSuccess);
        var design = new FormPdfDesign(new Dictionary<string, string> { ["es"] = "Constancia", ["en"] = "Receipt" }, [FieldId], false, null, true);

        var result = await fixture.Service.PreviewAsync(definition.Id, new(design, culture), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess); await result.Value.Content.DisposeAsync();
        using var values = JsonDocument.Parse(fixture.Rendered!.Response.Record.ValuesJson);
        var answer = Assert.Single(values.RootElement.EnumerateArray());
        Assert.Equal(FieldId, answer.GetProperty("fieldId").GetGuid());
        Assert.NotNull(FormLocationPolicy.Parse(answer.GetProperty("value").GetRawText()));
        Assert.Equal(0, fixture.Rules.UnitOfWork.Transactions); Assert.Empty(fixture.Designs);
    }

    private static CanonicalFormField Field() =>
        new(FieldId, "field_" + FieldId.ToString("N"), "location", new Dictionary<string, string> { ["es"] = "Dónde estás", ["en"] = "Where you are" },
            null, false, null, null);

    private static FormVersionSchema Schema() => new(1, [Field()]);

    private static FormRecordStoredRow Row(string location) => new(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "VIS", Guid.NewGuid(), 1,
        JsonSerializer.Serialize(new Dictionary<string, string> { ["es"] = "Visita", ["en"] = "Visit" }),
        JsonSerializer.Serialize(Schema(), JsonSerializerOptions.Web),
        "[{\"fieldId\":\"" + FieldId.ToString("D") + "\",\"value\":" + location + "}]");

    private static FormActionRecordRow Snapshot(FormRecordStoredRow row) => new(new(row,
        new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc), new(Guid.NewGuid(), "Author")), "es");

    private static (FormExportService Service, FakeFormWorkbookWriter Writer, FakeFormResponseReader Reader) Create()
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport];
        var writer = new FakeFormWorkbookWriter();
        var reader = new FakeFormResponseReader();
        var service = new FormExportService(reader, writer, new(user, permissions, reader),
            RequestValidators.For(new FormExportRequestValidator(), new ListFormRecordsRequestValidator()), new FakeTimeProvider(),
            NullLogger<FormExportService>.Instance, new(new(Array.Empty<IStructuredRecordOriginSource>()), new InMemoryUserAccounts()),
            new FormRecordResultReaderDouble());
        return (service, writer, reader);
    }
}
