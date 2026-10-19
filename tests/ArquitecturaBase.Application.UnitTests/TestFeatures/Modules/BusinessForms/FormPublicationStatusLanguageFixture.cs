using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormPublicationStatusLanguageFixture : IFormPublicationReader, IFormPublicationProtector
{
    public FormPublicationStatusLanguageFixture(string state, IReadOnlyList<string> cultures)
    {
        var operationId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var acceptedAtUtc = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        Stored = new(operationId, ActorId, RequestId, DefinitionId, versionId, [11, 22, 33],
            "{\"format\":1,\"questions\":[]}", state);
        Status = new(RequestId, operationId, DefinitionId, versionId, 2, 7, state, "Verify",
            acceptedAtUtc, acceptedAtUtc, null, false, false, []);
        // Complete texts do not authorize a culture omitted from the sealed profile.
        var names = new Dictionary<string, string> { ["es"] = "Visita", ["en"] = "Visit" };
        var buttons = new Dictionary<string, string> { ["es"] = "Enviar", ["en"] = "Submit" };
        Manifest = new(1, "attempt-fingerprint", "publication-fingerprint", DefinitionId, versionId, 7,
            "content-hash", names, "VISIT", cultures[0], buttons,
            [new("phone", "flow-static-7.3-r8", cultures.Select(culture =>
                new FormPublicationCultureProfile(culture, "compilation-" + culture)).ToArray())]);
    }

    public Guid ActorId { get; } = Guid.NewGuid();
    public Guid DefinitionId { get; } = Guid.NewGuid();
    public Guid RequestId { get; } = Guid.NewGuid();
    public FormPublicationStatusResponse Status { get; }
    public FormPublicationStoredRow Stored { get; }
    public FormPublicationManifest Manifest { get; }
    public Guid? QueriedDefinitionId { get; private set; }
    public Guid? QueriedActorId { get; private set; }
    public Guid? QueriedRequestId { get; private set; }
    public Guid? QueriedOperationId { get; private set; }
    public FormPublicationProtectionContext? ManifestContext { get; private set; }
    public byte[]? ManifestBytes { get; private set; }
    public int ManifestReads { get; private set; }

    public Task<FormPublicationStatusResponse?> FindOperationAsync(Guid definitionId, Guid actor, Guid requestId,
        CancellationToken cancellationToken)
    {
        QueriedDefinitionId = definitionId;
        QueriedActorId = actor;
        QueriedRequestId = requestId;
        return Task.FromResult<FormPublicationStatusResponse?>(Status);
    }

    public Task<FormPublicationStoredRow?> FindStoredAsync(Guid operationId, CancellationToken cancellationToken)
    {
        QueriedOperationId = operationId;
        return Task.FromResult<FormPublicationStoredRow?>(Stored);
    }

    public FormPublicationManifest ReadManifest(FormPublicationProtectionContext context, byte[] protectedManifest)
    {
        ManifestReads++;
        ManifestContext = context;
        ManifestBytes = protectedManifest;
        return Manifest;
    }

    public Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<FormPublicationStoredRow?> FindVersionAsync(Guid definitionId, Guid versionId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<FormPublicationStoredRow?> FindActiveVersionAsync(Guid definitionId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<bool> ExistsOpenOperationAsync(Guid definitionId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task<int> CountVersionsAsync(Guid definitionId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public string ProtectTicket(FormPublicationTicket ticket) => throw new NotSupportedException();
    public FormPublicationTicket? ReadTicket(string token) => throw new NotSupportedException();
    public byte[] ProtectManifest(FormPublicationProtectionContext context, FormPublicationManifest manifest) =>
        throw new NotSupportedException();
}
