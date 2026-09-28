using ArquitecturaBase.Application.Common.Validation;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

internal sealed class ListWidgetsRequestValidator()
    : PagedRequestValidator<ListWidgetsRequest>(ListWidgetsRequest.SortableFields);
