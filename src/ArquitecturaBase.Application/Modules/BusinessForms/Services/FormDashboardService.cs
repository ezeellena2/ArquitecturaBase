using System.Globalization;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
/// <summary>
/// The dashboard of a form (DOC7-A), read like the sheet: «Ver respuestas», or «Completar formularios» on one's own responses, the
/// sheet's filter and the permission checked again before answering. The reader adds up in SQL, one statement per family; the
/// buckets and the previous range go in the zone the front builds that filter in (<see cref="FormDashboardZonePolicy"/>).
/// </summary>
internal sealed class FormDashboardService(IFormResponseReader responses, IFormDashboardReader reader, FormRecordAccessGuard access,
    IRequestValidator validator, FormDashboardZonePolicy zones, IEnumerable<IStructuredFormStandardSource> sources, TimeProvider timeProvider,
    ILogger<FormDashboardService> logger) : IFormDashboardService
{
    public Task<Result<FormDashboardResponse>> SearchAsync(Guid id, FormDashboardRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormDashboardResponse>>(logger, "SearchFormDashboard", async () =>
        {
            var read = await access.CheckReadAsync(cancellationToken); if (read.IsFailure) return read.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            if (!FormDashboardRequest.Periods.Contains(request.Period, StringComparer.Ordinal)) return FormRecordErrors.PeriodInvalid;
            if (await validator.ValidateAsync(request.Filter, cancellationToken) is { } invalidFilter) return invalidFilter;
            var definition = await responses.FindDefinitionAsync(id, cancellationToken);
            if (definition is null) return FormRecordErrors.NotFound;
            var valid = FormRecordFilterPolicy.Validate(request.Filter, definition); if (valid.IsFailure) return valid.Error;
            var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
            var plan = FormDashboardPolicy.Plan(definition, culture, FormDashboardCurrencyPolicy.Find(definition, sources.SelectMany(source => source.Standards)));
            var (zone, zoneId) = await zones.FindAsync(read.Value.Actor, cancellationToken);
            var filter = read.Value.Apply(request.Filter) with { DisplayCulture = culture };
            var totals = await reader.FindTotalsAsync(definition, filter, plan.Measures, plan.Dates.Select(date => date.FieldId).ToArray(), cancellationToken);
            var range = FormDashboardPeriodPolicy.Previous(filter.ReceivedFromUtc, filter.ReceivedToUtc, zone);
            var previous = range is { } before
                ? await reader.FindTotalsAsync(definition, filter with { ReceivedFromUtc = before.FromUtc, ReceivedToUtc = before.ToUtc }, plan.Measures, [], cancellationToken)
                : null;
            var window = FormDashboardPeriodPolicy.Window(request.Period, zone, filter.ReceivedFromUtc, filter.ReceivedToUtc,
                totals.FirstAtUtc, totals.LastAtUtc, timeProvider.GetUtcNow().UtcDateTime);
            var dates = totals.Dates.ToDictionary(date => date.FieldId, date => FormDashboardPeriodPolicy.DateWindow(date.First, date.Last, request.Period));
            // Without responses nothing else adds up: the series of a range still shows its days, in zero.
            var any = totals.Count > 0;
            var buckets = any && window.Starts.Count > 0
                ? await reader.ListBucketsAsync(definition, filter, new(request.Period, zoneId, window.Starts[0]), plan.Measures, cancellationToken) : [];
            var shown = dates.Where(date => date.Value.Starts.Count > 0).Select(date => new FormDashboardDateWindow(date.Key, date.Value.Starts[0])).ToArray();
            var dateBuckets = any && shown.Length > 0
                ? await reader.ListDateBucketsAsync(definition, filter, request.Period, shown, plan.Measures, cancellationToken) : [];
            var distributions = any && plan.Questions.Count > 0
                ? await reader.ListDistributionsAsync(definition, filter, plan.Questions.Select(question => question.FieldId).ToArray(), FormDashboardPolicy.ChoiceLabels(plan),
                    plan.Measures, FormDashboardPolicy.Top, cancellationToken) : [];
            var items = FormDashboardPolicy.CatalogItems(plan, distributions, totals);
            var labels = items.ItemIds.Count > 0 ? await reader.ListCatalogLabelsAsync(definition, filter, items.FieldIds, items.ItemIds, cancellationToken) : [];
            var actors = any ? await reader.FindActorsAsync(definition, filter, FormDashboardPolicy.Top, cancellationToken) : new([], 0, 0);
            var states = any ? await reader.ListStatesAsync(definition, filter, cancellationToken) : [];
            if (!await access.StillAllowedAsync(read.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
            var numbers = FormDashboardPolicy.Numbers(plan, totals, previous, labels);
            return new FormDashboardResponse(definition.Id, request.Period, zoneId, totals.Count,
                range is { } compared && previous is not null ? new FormDashboardPreviousResponse(compared.FromUtc, compared.ToUtc, previous.Count) : null,
                numbers, FormDashboardPolicy.Series(window, buckets, numbers), FormDashboardPolicy.DateSeries(plan, dates, dateBuckets, numbers),
                FormDashboardPolicy.Distributions(plan, definition, distributions, labels, numbers, culture), FormDashboardPolicy.Actors(actors, culture),
                FormDashboardPolicy.States(states));
        });
}
