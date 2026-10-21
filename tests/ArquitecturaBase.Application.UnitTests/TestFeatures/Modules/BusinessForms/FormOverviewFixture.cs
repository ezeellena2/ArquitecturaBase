using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormOverviewFixture : IFormOverviewReader, IDisposable
{
    private readonly ServiceProvider _provider;
    public InMemoryUserAccounts Users { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public FakePermissionService Permissions { get; } = new();
    public Guid Actor { get; }
    public int Reads { get; private set; }
    public ListFormOverviewRequest? Request { get; private set; }
    public bool CanRead { get; private set; }
    public bool CanDecide { get; private set; }
    public Action? OnRead { get; set; }
    public FormOverviewFixture(bool read = true, bool decide = true)
    {
        Actor = Users.AddUser("overview@example.invalid").Id;
        Current.UserId = Actor;
        Permissions.Permissions[Actor] = [];
        if (read) Permissions.Permissions[Actor] = [.. Permissions.Permissions[Actor], BusinessFormsPermissions.RecordsRead];
        if (decide) Permissions.Permissions[Actor] = [.. Permissions.Permissions[Actor], BusinessFormsPermissions.ApprovalsDecide];
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBusinessFormsApplication();
        services.AddSingleton<ICurrentUser>(Current);
        services.AddSingleton<IPermissionService>(Permissions);
        services.AddSingleton<IUserReader>(Users);
        services.AddSingleton<IFormOverviewReader>(this);
        services.AddScoped<IRequestValidator, RequestValidator>();
        _provider = services.BuildServiceProvider();
    }
    public IFormOverviewService Service => _provider.GetRequiredService<IFormOverviewService>();
    public IRequestValidator Validator => _provider.GetRequiredService<IRequestValidator>();
    public Task<FormOverviewResponse> FindPageAsync(Guid actorUserId, ListFormOverviewRequest request, string culture,
        bool canReadRecords, bool canDecide, CancellationToken cancellationToken)
    {
        if (actorUserId != Actor) throw new InvalidOperationException("The reader must receive the authorized actor.");
        cancellationToken.ThrowIfCancellationRequested();
        Reads++; Request = request; CanRead = canReadRecords; CanDecide = canDecide;
        OnRead?.Invoke();
        return Task.FromResult(new FormOverviewResponse(new([], request.Page, request.PageSize, 0),
            new(0, 0, canDecide ? 0 : null)));
    }
    public void Dispose() => _provider.Dispose();
}