using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormAssistantFixture : IFormSelectionReader, IFormMissingRequestReader
{
    public FormAssistantFixture()
    {
        UserId = Users.AddUser("recipient@example.invalid").Id;
        Permissions.Permissions[UserId] = [BusinessFormsPermissions.RecordsCreate];
        Catalog.Items.Add(BusinessFormCatalogItem.Create(Catalog.Catalog.Id, "A", "Ana", "Anne", null, null, 0));
        Rows.Add(new(Catalog.DefinitionId, Catalog.Version.Id, 1, "VISIT", JsonSerializer.Serialize(FormCatalogPreparationFixture.Texts),
            JsonSerializer.Serialize(FormCatalogPreparationFixture.Texts), "es", 1, 3, "Cuando cuenta una visita", Catalog.Version.SchemaJson));
        Candidates = new(this, Users, Permissions, new(Catalog, Catalog), new(Catalog.Rules, Catalog));
    }
    public InMemoryUserAccounts Users { get; } = new();
    public FakePermissionService Permissions { get; } = new();
    public FormCatalogPreparationFixture Catalog { get; } = new();
    public Guid UserId { get; }
    public List<FormAssistantCandidateRow> Rows { get; } = [];
    public FormAssistantCandidatePolicy Candidates { get; }
    public BusinessFormAssistantCapabilitySource Source => new(Candidates, this);
    public FormAssistantPrefillPolicy Prefill => new(Candidates, this, Catalog.Policy);
    public AssistantChannelContext Context => new("whatsapp-assistant", UserId, UserId, "es", "America/Argentina/Buenos_Aires", new([], []),
        [new(BusinessFormAssistantCapabilitySource.CandidatesId, 1), new(BusinessFormAssistantCapabilitySource.RegistrationId, 1)], [new("assistant_proposal", 1)]);
    public FormAssistantCandidatesPayload ReadPayload(Guid? recipient = null)
    {
        var row = Rows[0];
        var choices = Catalog.Items.Where(item => item.IsActive).Select(item => new FormAssistantChoice(item.Id, item.TextEs)).ToArray();
        return new("complete", recipient ?? UserId, "es", true, [new(row.DefinitionId, row.VersionId, row.DefinitionRevision, "Personas", row.OfferWhen, row.Culture,
            [new(Catalog.FieldId, "choice", "Personas", null, Catalog.Catalog.Id, Catalog.Catalog.Revision, choices)])], []);
    }
    public AssistantObservedRead Read(Guid turnId, Guid? recipient = null, int ordinal = 1) =>
        new(turnId, ordinal, new(BusinessFormAssistantCapabilitySource.CandidatesId, 1), ReadPayload(recipient));
    public FormAssistantRegistrationPayload Registration(int ordinal = 1) => new("proposed", ordinal, Rows[0].DefinitionId, Rows[0].VersionId,
        Rows[0].DefinitionRevision, [new(Catalog.FieldId, "choice", Catalog.Items[0].Id.ToString("D"))], "Contó una visita.");
    public AssistantObservedProposal Proposal(Guid? turn = null)
    {
        var turnId = turn ?? Guid.CreateVersion7();
        return new(turnId, Guid.CreateVersion7(), new(BusinessFormAssistantCapabilitySource.RegistrationId, 1), Registration()) { ObservedReads = [Read(turnId)] };
    }
    public List<FormMissingTopicMetadata> Topics { get; } = [];
    public Task<IReadOnlyList<FormMissingTopicMetadata>> ListAssistantTopicsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<FormMissingTopicMetadata>>(Topics.ToArray());
    public Task<MissingFormTopicsPageResponse> FindPageAsync(ListMissingFormTopicsRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<MissingFormTopicResponse?> FindAsync(Guid topicId, CancellationToken ct) => throw new NotSupportedException();
    public Action? OnRead { get; set; }
    public int Reads { get; private set; }
    public Task<IReadOnlyList<FormAssistantCandidateRow>> ListSuggestionTargetsAsync(IReadOnlyCollection<Guid> definitionIds, CancellationToken ct)
    { Reads++; OnRead?.Invoke(); return Task.FromResult<IReadOnlyList<FormAssistantCandidateRow>>(Rows.Where(row => definitionIds.Contains(row.DefinitionId)).ToArray()); }
    public Task<IReadOnlyList<FormAssistantCandidateRow>> ListAssistantAsync(CancellationToken ct)
    { Reads++; OnRead?.Invoke(); return Task.FromResult<IReadOnlyList<FormAssistantCandidateRow>>(Rows.ToArray()); }
    public Task<FormAssistantCandidateRow?> FindAssistantAsync(Guid definitionId, Guid versionId, CancellationToken ct)
    { Reads++; OnRead?.Invoke(); return Task.FromResult(Rows.SingleOrDefault(row => row.DefinitionId == definitionId && row.VersionId == versionId)); }
    public Task<StructuredFormSelectionRow?> FindAsync(Guid definitionId, Guid versionId, CancellationToken ct) => throw new NotSupportedException();
    public Task<StructuredFormSelectionRow?> FindPublishedAsync(Guid definitionId, Guid versionId, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<StructuredFormSelectionRow>> ListAsync(string culture, ListStructuredFormsRequest request, CancellationToken ct) => throw new NotSupportedException();
}
