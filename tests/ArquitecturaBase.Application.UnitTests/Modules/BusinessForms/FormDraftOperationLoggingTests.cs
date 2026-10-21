using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormDraftOperationLoggingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_authorization_failure_is_logged_before_validation_without_recording_the_request()
    {
        var logger = new FakeLogger<FormDraftService>();
        var validator = new ThrowingValidator();
        var user = new FakeCurrentUser();
        var service = Service(logger, validator, user, new FakePermissionService());

        var result = await service.CreateAsync(null!, Ct);

        Assert.Equal(FormDefinitionErrors.Unauthorized.Code, result.Error.Code);
        Assert.Equal(
            ["Handling CreateFormDraft", $"CreateFormDraft failed with {FormDefinitionErrors.Unauthorized.Code}"],
            logger.Collector.GetSnapshot().Select(record => record.Message));
        Assert.False(validator.WasCalled);
    }

    [Fact]
    public async Task A_successful_public_service_call_logs_handling_and_handled_without_request_data()
    {
        var logger = new FakeLogger<FormDraftService>();
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.Read];
        var service = Service(logger, new AcceptingValidator(), user, permissions, new StubFormDefinitionReader());

        const string privateSearch = "contacto-privado-9841";
        var result = await service.GetFacetsAsync(new ListFormDefinitionsRequest { Search = privateSearch }, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(new FormDefinitionFacetsResponse(4, 1, 3, 0), result.Value);
        var messages = logger.Collector.GetSnapshot().Select(record => record.Message).ToArray();
        Assert.Equal(["Handling GetFormDefinitionFacets", "Handled GetFormDefinitionFacets"], messages);
        Assert.DoesNotContain(messages, message => message.Contains(privateSearch, StringComparison.Ordinal));
    }

    private static FormDraftService Service(FakeLogger<FormDraftService> logger, IRequestValidator validator,
        FakeCurrentUser user, IPermissionService permissions, IFormDefinitionReader? reader = null) => new(
        null!, new FormDefinitionReadPolicy(reader!, null!, new FormDraftAccessGuard(user, permissions), null!, TimeProvider.System), null!, validator, new FormDraftAccessGuard(user, permissions), null!, TimeProvider.System, logger);

    private sealed class ThrowingValidator : IRequestValidator
    {
        public bool WasCalled { get; private set; }

        public Task<ValidationError?> ValidateAsync<TRequest>(TRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Validation must not run before authorization.");
        }
    }

    private sealed class AcceptingValidator : IRequestValidator
    {
        public Task<ValidationError?> ValidateAsync<TRequest>(TRequest request, CancellationToken cancellationToken) => Task.FromResult<ValidationError?>(null);
    }

    private sealed class StubFormDefinitionReader : IFormDefinitionReader
    {
        public Task<FormDefinitionCounts> CountFacetsAsync(string? search, string displayCulture, CancellationToken cancellationToken) =>
            Task.FromResult(new FormDefinitionCounts(4, 1, 3, 0));

        public Task<FormChangeStoredRow?> FindChangeAsync(Guid actor, Guid requestId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<FormDefinitionStoredRow?> FindAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<FormPublicationOverviewResponse> FindPublicationAsync(Guid id, Guid? recoveryActor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PagedResult<FormDefinitionListStoredRow>> ListAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
