using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class UserServiceWriteIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_and_update_commit_Identity_changes_across_scopes()
    {
        var email = TestEmails.Unique("user-service-write");
        var phone = TestPhones.Unique();
        var created = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserService>()
            .CreateUserAsync(new CreateUserRequest(email, "Antes", null,
                Phone: new PhoneNumberInput("AR", TestPhones.AsTypedLocally(phone))), Ct));
        Assert.True(created.IsSuccess);

        var byEmail = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserReader>()
            .FindByEmailAsync(Email.Create(email.ToUpperInvariant()).Value, Ct));
        Assert.Equal(created.Value, byEmail?.Id);

        var updated = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserService>()
            .UpdateUserAsync(new UpdateUserRequest(created.Value, "Después", [SystemRoles.User]), Ct));
        Assert.True(updated.IsSuccess);

        var detail = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserService>()
            .GetUserAsync(created.Value, Ct));
        Assert.True(detail.IsSuccess);
        Assert.Equal(email, detail.Value.Email);
        Assert.False(detail.Value.EmailConfirmed);
        Assert.Equal(phone.Value, detail.Value.PhoneNumber);
        Assert.False(detail.Value.PhoneNumberConfirmed);
        Assert.Equal("Después", detail.Value.DisplayName);
        Assert.Equal([SystemRoles.User], detail.Value.Roles);
    }

    [Fact]
    public async Task Invalid_update_does_not_change_a_persisted_user()
    {
        var email = TestEmails.Unique("user-service-invalid");
        var created = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserService>()
            .CreateUserAsync(new CreateUserRequest(email, "Original", null), Ct));
        Assert.True(created.IsSuccess);

        var updated = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserService>()
            .UpdateUserAsync(new UpdateUserRequest(created.Value, "No guardar", Roles: null), Ct));
        Assert.IsType<ValidationError>(updated.Error);

        var detail = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUserService>()
            .GetUserAsync(created.Value, Ct));
        Assert.Equal("Original", detail.Value.DisplayName);
    }
}
