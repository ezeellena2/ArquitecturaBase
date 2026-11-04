namespace ArquitecturaBase.Api.Modules.Arca.Contracts.Connection;

public sealed record ChooseArcaServicesHttpRequest(IReadOnlyList<string>? Services, long ExpectedRevision);
