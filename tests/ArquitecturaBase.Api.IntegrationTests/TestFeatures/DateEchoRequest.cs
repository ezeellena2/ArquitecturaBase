namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

public sealed record DateEchoRequest(DateTime OccurredAtUtc, DateTime? ExpiresAtUtc);
