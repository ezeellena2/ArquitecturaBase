namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

public sealed record WidgetDetailResponse(
    Guid Id,
    string Name,
    DateTime CreatedAtUtc,
    Guid? CreatedBy,
    DateTime? ModifiedAtUtc,
    Guid? ModifiedBy);
