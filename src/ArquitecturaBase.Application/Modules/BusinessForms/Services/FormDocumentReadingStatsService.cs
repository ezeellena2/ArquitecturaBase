using System.Globalization;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «Cómo viene leyendo» (DOC1, decision 3 of the user): with «Ver formularios», the documents confirmed into the form over the last 30
/// days and, per question, how many values the reading prefilled and how many were confirmed without changes, with the question corrected
/// most often and the questions that mark a repeated document (<see cref="FormDocumentReadingStatsPolicy"/>). The latest corrections
/// are values of responses: only with «Ver respuestas», checked before reading them and again before answering, and without it only
/// the numbers are seen. Nothing goes to the provider. It only reads, without a unit of work.
/// </summary>
internal sealed class FormDocumentReadingStatsService(IFormDocumentReadingReader reader, FormDraftAccessGuard access,
    IEnumerable<IStructuredFormStandardSource> sources, TimeProvider clock, ILogger<FormDocumentReadingStatsService> logger) : IFormDocumentReadingStatsService
{
    public Task<Result<FormDocumentReadingStatsResponse>> GetAsync(Guid definitionId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormDocumentReadingStatsResponse>>(logger, "GetFormDocumentReadingStats", async () =>
        {
            var allowed = await access.CheckAsync(false, cancellationToken); if (allowed.IsFailure) return allowed.Error;
            if (await reader.FindDefinitionAsync(definitionId, cancellationToken) is not { } definition) return FormDefinitionErrors.NotFound;
            var sinceUtc = clock.GetUtcNow().UtcDateTime.AddDays(-FormDocumentReadingStatsResponse.WindowDays);
            var stats = await reader.FindStatsAsync(definitionId, sinceUtc, cancellationToken);
            IReadOnlyList<FormDocumentReadingExampleResponse>? examples = null;
            if (await access.HasPermissionAsync(allowed.Value, BusinessFormsPermissions.RecordsRead, cancellationToken))
            {
                var corrections = await reader.ListCorrectionsAsync(definitionId, sinceUtc, FormDocumentReadingStatsResponse.MaxExamples, cancellationToken);
                var schemas = corrections.Count == 0 ? new Dictionary<Guid, string>()
                    : await reader.ListVersionSchemasAsync(corrections.Select(row => row.VersionId).Distinct().ToArray(), cancellationToken);
                var questions = schemas.ToDictionary(pair => pair.Key, pair => FormDraftPolicy.Read<FormVersionSchema>(pair.Value).Questions);
                var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
                examples = corrections.Select(row => FormDocumentReadingStatsPolicy.Example(row, definition.Prefix,
                    questions.GetValueOrDefault(row.VersionId)?.FirstOrDefault(question => question.FieldId == row.FieldId), culture)).ToArray();
                if (!await access.HasPermissionAsync(allowed.Value, BusinessFormsPermissions.RecordsRead, cancellationToken)) examples = null;
            }
            var current = await access.CheckAsync(false, cancellationToken);
            if (current.IsFailure || current.Value != allowed.Value) return FormDefinitionErrors.Forbidden;
            return new FormDocumentReadingStatsResponse(sinceUtc, stats.Documents, stats.Questions, FormDocumentReadingStatsPolicy.MostCorrected(stats.Questions),
                FormDocumentReadingStatsPolicy.DuplicateQuestionIds(definition, sources.SelectMany(source => source.Standards))) { Examples = examples };
        });
}
