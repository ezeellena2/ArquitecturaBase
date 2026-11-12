using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// DOC1, «Cómo viene leyendo»: the numbers of the last 30 days need «Ver formularios»; the latest corrections are values of responses,
// so they also need «Ver respuestas», checked before reading them and again before answering, and their key is omitted without it.
// The questions that mark a repeated document come from the standard of the copy, through its sealed bound map.
public sealed class FormDocumentReadingStatsServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Definition = Guid.CreateVersion7(), Version = Guid.CreateVersion7(), Account = Guid.CreateVersion7(),
        Amount = Guid.CreateVersion7(), Date = Guid.CreateVersion7(), Operation = Guid.CreateVersion7();
    private static readonly IReadOnlyDictionary<string, string> Label = new Dictionary<string, string> { ["es"] = "Dato" };

    private sealed class Reader : IFormDocumentReadingReader
    {
        public FormDocumentReadingDefinitionRow? Definition { get; set; } = new("REC", "forms.transfer-receipt", 1,
            FormDraftPolicy.Serialize(new Dictionary<string, Guid> { ["sourceAccount"] = Account, ["amount"] = Amount, ["date"] = Date, ["operationNumber"] = Operation }));
        public Action? OnCorrections { get; set; }
        public DateTime? Since { get; private set; }
        public int? Limit { get; private set; }
        public int CorrectionReads { get; private set; }

        public Task<FormDocumentReadingDefinitionRow?> FindDefinitionAsync(Guid definitionId, CancellationToken cancellationToken) =>
            Task.FromResult(definitionId == FormDocumentReadingStatsServiceTests.Definition ? Definition : null);

        public Task<FormDocumentReadingStatsRow> FindStatsAsync(Guid definitionId, DateTime sinceUtc, CancellationToken cancellationToken)
        {
            Since = sinceUtc;
            return Task.FromResult(new FormDocumentReadingStatsRow(4, [new(Account, 4, 1), new(Amount, 4, 4), new(Date, 3, 2)]));
        }

        public Task<IReadOnlyList<FormDocumentReadingCorrectionRow>> ListCorrectionsAsync(Guid definitionId, DateTime sinceUtc, int limit,
            CancellationToken cancellationToken)
        {
            CorrectionReads++;
            Limit = limit;
            OnCorrections?.Invoke();
            return Task.FromResult<IReadOnlyList<FormDocumentReadingCorrectionRow>>(
            [
                new(Guid.CreateVersion7(), 123, Version, Now.UtcDateTime.AddDays(-1), Amount, "150000.5", "1500000.5", null, null),
                new(Guid.CreateVersion7(), 122, Version, Now.UtcDateTime.AddDays(-2), Date, "2026-11-01", "2026-11-10", null, null),
            ]);
        }

        public Task<IReadOnlyDictionary<Guid, string>> ListVersionSchemasAsync(IReadOnlyCollection<Guid> versionIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>
            {
                [Version] = FormDraftPolicy.Serialize(new FormVersionSchema(1,
                [
                    new CanonicalFormField(Amount, "field_a", "decimal", Label, null, true, null, null),
                    new CanonicalFormField(Date, "field_d", "date", Label, null, true, null, null),
                ])),
            });
    }

    private static (FormDocumentReadingStatsService Service, Reader Reader, FakePermissionService Permissions, Guid Actor) Create(params string[] permissions)
    {
        var actor = Guid.CreateVersion7();
        var granted = new FakePermissionService();
        granted.Permissions[actor] = permissions;
        var reader = new Reader();
        var service = new FormDocumentReadingStatsService(reader, new FormDraftAccessGuard(new FakeCurrentUser { UserId = actor }, granted),
            [new BusinessFormStandardSource()], new FakeTimeProvider(Now), NullLogger<FormDocumentReadingStatsService>.Instance);
        return (service, reader, granted, actor);
    }

    [Fact]
    public async Task The_numbers_of_the_last_thirty_days_name_the_most_corrected_question_and_the_duplicates_without_examples_without_records_read()
    {
        var (service, reader, _, _) = Create(BusinessFormsPermissions.Read);

        var result = await service.GetAsync(Definition, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal((Now.UtcDateTime.AddDays(-30), 4), (result.Value.SinceUtc, result.Value.Documents));
        Assert.Equal(Now.UtcDateTime.AddDays(-30), reader.Since);
        Assert.Equal(new FormDocumentReadingCorrectedResponse(Account, 3), result.Value.MostCorrected);
        Assert.Equal(3, result.Value.Questions.Count);
        // The standard says the operation number, the amount and the date mark a repeated receipt.
        Assert.Equal([Operation, Amount, Date], result.Value.DuplicateQuestionIds);
        Assert.Null(result.Value.Examples);
        Assert.Equal(0, reader.CorrectionReads);
        var json = FormDraftPolicy.Serialize(result.Value);
        Assert.DoesNotContain("examples", json, StringComparison.Ordinal);
        Assert.Contains("\"mostCorrected\":{", json, StringComparison.Ordinal);
        Assert.Contains("\"sinceUtc\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_records_read_it_also_lists_the_latest_corrections_as_display_text()
    {
        var (service, reader, _, _) = Create(BusinessFormsPermissions.Read, BusinessFormsPermissions.RecordsRead);
        var culture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es");
        try
        {
            var result = await service.GetAsync(Definition, Ct);

            Assert.Equal(FormDocumentReadingStatsResponse.MaxExamples, reader.Limit);
            Assert.Equal([("REC-000123", Amount, "150.000,5", "1.500.000,5"), ("REC-000122", Date, "01/11/2026", "10/11/2026")],
                result.Value.Examples!.Select(example => (example.RecordNumber, example.QuestionId, example.Read, example.Confirmed)));
        }
        finally { CultureInfo.CurrentUICulture = culture; }
    }

    [Fact]
    public async Task Records_read_revoked_while_reading_drops_the_examples_and_read_revoked_forbids()
    {
        var (service, reader, permissions, actor) = Create(BusinessFormsPermissions.Read, BusinessFormsPermissions.RecordsRead);
        reader.OnCorrections = () => permissions.Permissions[actor] = [BusinessFormsPermissions.Read];

        var withoutExamples = await service.GetAsync(Definition, Ct);

        Assert.True(withoutExamples.IsSuccess);
        Assert.Null(withoutExamples.Value.Examples);

        permissions.Permissions[actor] = [BusinessFormsPermissions.Read, BusinessFormsPermissions.RecordsRead];
        reader.OnCorrections = () => permissions.Permissions[actor] = [];
        Assert.Equal(FormDefinitionErrors.Forbidden.Code, (await service.GetAsync(Definition, Ct)).Error.Code);
    }

    [Fact]
    public async Task Without_read_or_with_another_form_it_answers_nothing_and_an_own_form_marks_no_duplicates()
    {
        var (forbidden, _, _, _) = Create(BusinessFormsPermissions.RecordsRead);
        Assert.Equal(FormDefinitionErrors.Forbidden.Code, (await forbidden.GetAsync(Definition, Ct)).Error.Code);

        var (service, reader, _, _) = Create(BusinessFormsPermissions.Read);
        Assert.Equal(FormDefinitionErrors.NotFound.Code, (await service.GetAsync(Guid.CreateVersion7(), Ct)).Error.Code);
        reader.Definition = new("PAG", null, null, null);
        Assert.Empty((await service.GetAsync(Definition, Ct)).Value.DuplicateQuestionIds);
    }

    [Fact]
    public void Without_corrections_no_question_is_the_most_corrected_and_a_value_shows_as_a_message_shows_it()
    {
        Assert.Null(FormDocumentReadingStatsPolicy.MostCorrected([new(Account, 2, 2)]));
        Assert.Null(FormDocumentReadingStatsPolicy.MostCorrected([]));
        // Ties go to the lowest id.
        var (first, second) = Account.CompareTo(Amount) < 0 ? (Account, Amount) : (Amount, Account);
        Assert.Equal(first, FormDocumentReadingStatsPolicy.MostCorrected([new(second, 3, 1), new(first, 2, 0)])!.QuestionId);

        var yesNo = new CanonicalFormField(Guid.CreateVersion7(), "field_b", "boolean", Label, null, false, null, null);
        var choiceId = Guid.CreateVersion7();
        var own = new CanonicalFormField(Guid.CreateVersion7(), "field_c", "choice", Label, null, false, null,
            [new CanonicalFormChoice(choiceId, new Dictionary<string, string> { ["es"] = "Pesos", ["en"] = "Pesos" })]);
        var catalog = new CanonicalFormField(Guid.CreateVersion7(), "field_k", "choice", Label, null, false, null, null, new FormQuestionSource(Guid.CreateVersion7()));
        Assert.Equal("Sí", FormDocumentReadingStatsPolicy.Text(yesNo, "true", null, "es"));
        Assert.Equal("Pesos", FormDocumentReadingStatsPolicy.Text(own, choiceId.ToString("D"), null, "en"));
        Assert.Equal("Banco Macro", FormDocumentReadingStatsPolicy.Text(catalog, Guid.CreateVersion7().ToString("D"), "Banco Macro", "es"));
        Assert.Equal("11/10/2026", FormDocumentReadingStatsPolicy.Text(new CanonicalFormField(Date, "field_d", "date", Label, null, true, null, null), "2026-11-10", null, "en"));
        Assert.Equal("", FormDocumentReadingStatsPolicy.Text(yesNo, null, null, "es"));
        Assert.Equal("0170 0992", FormDocumentReadingStatsPolicy.Text(null, "0170 0992", null, "es"));
    }
}
