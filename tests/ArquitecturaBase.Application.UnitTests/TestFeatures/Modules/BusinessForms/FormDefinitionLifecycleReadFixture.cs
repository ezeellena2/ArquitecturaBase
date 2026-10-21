using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormDefinitionLifecycleReadFixture : IFormDefinitionReader, IFormLifecycleReader, IStructuredFormChannelStateSource
{
    public FakeCurrentUser User { get; } = new() { UserId = Guid.NewGuid() };
    public FakePermissionService Permissions { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
    public Guid Id { get; } = Guid.NewGuid();
    public Guid OtherId { get; } = Guid.NewGuid();
    public FormDefinitionReadPolicy Policy { get; }
    public int FactReads { get; private set; }
    public IReadOnlyCollection<Guid> RequestedIds { get; private set; } = [];
    public Action? BeforeFactsReturn { get; set; }
    public Guid? RecoveryActor { get; private set; }
    public HashSet<Guid> MissingFactsIds { get; } = [];
    public long FirstRecordNumber { get; set; } = 10;
    public DateTime? LastOpenSessionExpiresAtUtc { get; set; }
    public bool HasPendingReplies { get; set; }
    public int StateReads { get; set; }
    public StructuredFormDeprecationState? DeprecationState { get; set; }
    public DateTime? DeprecatedAtUtc { get; set; }
    public FormDefinitionLifecycleReadFixture()
    {
        Permissions.Permissions[User.UserId!.Value] = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage,
            BusinessFormsPermissions.Publish, BusinessFormsPermissions.RecordsRead];
        Policy = new(this, this, new(User, Permissions), this, Clock);
    }
    public Task<FormDefinitionStoredRow?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<FormDefinitionStoredRow?>(id == Id
        ? new(Id, "{\"es\":\"Visita\"}", "VIS", true, 4, "es", "{\"es\":\"Enviar\"}", "{\"format\":1,\"fields\":[]}",
            AtUtc, null, AtUtc, new(User.UserId!.Value, "Publisher")) : null);
    public Task<FormPublicationOverviewResponse> FindPublicationAsync(Guid id, Guid? recoveryActor, CancellationToken ct)
    {
        RecoveryActor = recoveryActor;
        return Task.FromResult(new FormPublicationOverviewResponse(new(Guid.NewGuid(), 1, 1, ["es"]), null, "Published"));
    }
    public Task<PagedResult<FormDefinitionListStoredRow>> ListAsync(ListFormDefinitionsRequest request, CancellationToken ct) =>
        Task.FromResult(new PagedResult<FormDefinitionListStoredRow>([
            new(Id, new Dictionary<string, string> { ["es"] = "Visita" }, "VIS", 1, 1, "Published", AtUtc, null),
            new(OtherId, new Dictionary<string, string> { ["es"] = "Factura" }, "FAC", 2, 2, "Deactivated", AtUtc, null)], 3, 2, 7));
    public Task<IReadOnlyList<FormLifecycleStoredRow>> ListFactsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        FactReads++; RequestedIds = ids.ToArray();
        IReadOnlyList<FormLifecycleStoredRow> result = ids.Where(id => !MissingFactsIds.Contains(id)).Select(id => new FormLifecycleStoredRow(id, id == Id ? 4 : 5, id == Id ? "VIS" : "FAC",
            Guid.NewGuid(), id == Id ? 2 : 3, id == Id ? FirstRecordNumber : 20, id == Id ? 1 : 2, id == Id ? null : AtUtc,
            id == Id ? null : new(User.UserId!.Value, "Publisher"), false)).ToArray();
        BeforeFactsReturn?.Invoke();
        return Task.FromResult(result);
    }
    public Task<IReadOnlyList<StructuredFormChannelStateRow>> ListAsync(IReadOnlyCollection<Guid> ids, DateTime nowUtc, CancellationToken ct)
    {
        StateReads++;
        return Task.FromResult<IReadOnlyList<StructuredFormChannelStateRow>>(ids.Select(id =>
            new StructuredFormChannelStateRow(id, LastOpenSessionExpiresAtUtc, HasPendingReplies)
            { DeprecationState = DeprecationState, DeprecatedAtUtc = DeprecatedAtUtc }).ToArray());
    }
    public Task<FormChangeStoredRow?> FindChangeAsync(Guid actor, Guid requestId, CancellationToken ct) => throw new NotSupportedException();
    public Task<FormDefinitionCounts> CountFacetsAsync(string? search, string culture, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<FormIncomingSuggestionResponse>> ListIncomingSuggestionsAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    private static DateTime AtUtc => new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
}
