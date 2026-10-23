using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormRecipientPreviewFixture : IFormDefinitionReader, IFormCatalogFilterReader
{
    public FormCatalogPreparationFixture Catalogs { get; } = new();
    public FormCatalogPersonReader People { get; } = new();
    public InMemoryUserAccounts Users { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public FakePermissionService Permissions { get; } = new();
    public Guid Actor { get; }
    public Guid Subject { get; }
    public bool DefinitionMissing { get; set; }
    public bool SubjectMissing { get; set; }
    public int DefinitionReads { get; private set; }
    public int SampleReads { get; private set; }
    public Action? OnRead { get; set; }
    public FormRecipientPreviewService Service { get; }
    public FormRecipientPreviewFixture()
    {
        Actor = Users.AddUser("preview-manager@example.invalid").Id;
        Subject = Users.AddUser("preview-subject@example.invalid").Id;
        Current.UserId = Actor;
        Permissions.Permissions[Actor] = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage];
        Service = new(new(Current, Permissions), RequestValidators.For(new FormRecipientPreviewRequestValidator(), new ListFormRecipientSamplesRequestValidator()),
            this, Users, People, new(Catalogs, this, People), this);
    }
    public FormRecipientPreviewRequest Request(FormQuestionSource? source = null, string type = "choice") => new(type, source ?? new(Catalogs.Catalog.Id), Subject, "es");
    public Task<FormDefinitionStoredRow?> FindAsync(Guid id, CancellationToken ct)
    {
        DefinitionReads++;
        OnRead?.Invoke();
        return Task.FromResult<FormDefinitionStoredRow?>(DefinitionMissing || id != Catalogs.DefinitionId ? null : new(Catalogs.DefinitionId, Catalogs.Version.NameJson!,
            "REC", false, 1, "es", Catalogs.Version.SubmitLabelJson!, "{\"format\":1,\"fields\":[]}", DateTime.UnixEpoch, null, DateTime.UnixEpoch, null));
    }
    public Task<PagedResult<FormCatalogFilterSampleUserResponse>> ListSampleUsersAsync(FormCatalogFilterOptionsRequest request, CancellationToken ct)
    {
        SampleReads++;
        OnRead?.Invoke();
        return Task.FromResult(new PagedResult<FormCatalogFilterSampleUserResponse>([new(Subject, "Juan Gómez")], request.Page, request.PageSize, 1));
    }
    public Task<bool> ExistsActiveSampleUserAsync(Guid id, CancellationToken ct) => Task.FromResult(!SubjectMissing && id == Subject);
    public Task<FormPublicationOverviewResponse> FindPublicationAsync(Guid id, Guid? actor, CancellationToken ct) => throw new NotSupportedException();
    public Task<FormChangeStoredRow?> FindChangeAsync(Guid actor, Guid requestId, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormDefinitionListStoredRow>> ListAsync(ListFormDefinitionsRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<FormDefinitionCounts> CountFacetsAsync(string? search, string culture, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<FormCatalogFilterChoiceResponse>> ListRolesAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<FormCatalogFilterPreviewResponse> FindPreviewAsync(Guid id, FormCatalogFilterDefinition filters, CancellationToken ct) => throw new NotSupportedException();
}
