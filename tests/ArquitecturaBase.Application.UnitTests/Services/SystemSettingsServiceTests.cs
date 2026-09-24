using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Application.Services.Settings;
using ArquitecturaBase.Application.Validation.Settings;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.UnitTests.Services;

public sealed class SystemSettingsServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_returns_not_found_when_the_single_row_is_missing()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.GetAsync(Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(SettingsErrors.NotFound, result.Error);
        Assert.Equal(1, fixture.RepositoryGetCalls);
        Assert.Empty(fixture.Events);
    }

    [Theory]
    [InlineData(RegistrationMode.InviteOnly)]
    [InlineData(RegistrationMode.Open)]
    public async Task Get_returns_the_stored_registration_mode(RegistrationMode mode)
    {
        var fixture = new Fixture { Settings = SystemSettings.Create(mode) };

        var result = await fixture.Service.GetAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(mode, result.Value.RegistrationMode);
        Assert.Equal(1, fixture.RepositoryGetCalls);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task Update_returns_not_found_without_saving_when_the_single_row_is_missing()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.UpdateAsync(new UpdateSystemSettingsRequest(RegistrationMode.Open), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(SettingsErrors.NotFound, result.Error);
        Assert.Equal(1, fixture.RepositoryGetCalls);
        Assert.Empty(fixture.Events);
    }

    [Theory]
    [InlineData("es", "El modo de registro no es válido.")]
    [InlineData("en", "The registration mode is not valid.")]
    public async Task Update_validates_before_reading_or_saving(string culture, string message)
    {
        using var cultureScope = new CultureScope(culture);
        var fixture = new Fixture { Settings = SystemSettings.Create(RegistrationMode.InviteOnly) };

        var result = await fixture.Service.UpdateAsync(new UpdateSystemSettingsRequest((RegistrationMode)7), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(ValidationError.ErrorCode, error.Code);
        Assert.Equal([message], error.Errors["registrationMode"]);
        Assert.Equal(0, fixture.RepositoryGetCalls);
        Assert.Equal(RegistrationMode.InviteOnly, fixture.Settings.RegistrationMode);
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task Update_saves_the_changed_mode_once_before_invalidating_the_cache()
    {
        var fixture = new Fixture { Settings = SystemSettings.Create(RegistrationMode.InviteOnly) };

        var result = await fixture.Service.UpdateAsync(new UpdateSystemSettingsRequest(RegistrationMode.Open), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(RegistrationMode.Open, fixture.Settings.RegistrationMode);
        Assert.Equal(1, fixture.RepositoryGetCalls);
        Assert.Equal(["save", "invalidate"], fixture.Events);
    }

    [Fact]
    public async Task Update_does_not_invalidate_the_cache_when_saving_fails()
    {
        var fixture = new Fixture
        {
            Settings = SystemSettings.Create(RegistrationMode.InviteOnly),
            SaveException = new InvalidOperationException("Save failed."),
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.UpdateAsync(new UpdateSystemSettingsRequest(RegistrationMode.Open), Ct));

        Assert.Equal("Save failed.", error.Message);
        Assert.Equal(["save"], fixture.Events);
    }

    private sealed class Fixture
    {
        private readonly FakeRepository _repository;
        private readonly FakeUnitOfWork _unitOfWork;
        private readonly FakeReader _reader;

        public Fixture()
        {
            _repository = new FakeRepository(this);
            _unitOfWork = new FakeUnitOfWork(this);
            _reader = new FakeReader(this);
            Service = new SystemSettingsService(
                _repository,
                _reader,
                _unitOfWork,
                new ServiceRequestValidator<UpdateSystemSettingsRequest>(
                    [new UpdateSystemSettingsRequestValidator()]));
        }

        public SystemSettingsService Service { get; }

        public SystemSettings? Settings { get; set; }

        public Exception? SaveException { get; set; }

        public List<string> Events { get; } = [];

        public int RepositoryGetCalls => _repository.GetCalls;

        private sealed class FakeRepository(Fixture fixture) : ISystemSettingsRepository
        {
            public int GetCalls { get; private set; }

            public Task<SystemSettings?> GetAsync(CancellationToken cancellationToken)
            {
                GetCalls++;
                return Task.FromResult(fixture.Settings);
            }

            public void Add(SystemSettings settings) => throw new NotSupportedException();
        }

        private sealed class FakeReader(Fixture fixture) : ISystemSettingsReader
        {
            public Task<RegistrationMode> GetRegistrationModeAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task InvalidateAsync(CancellationToken cancellationToken)
            {
                fixture.Events.Add("invalidate");
                return Task.CompletedTask;
            }
        }

        private sealed class FakeUnitOfWork(Fixture fixture) : IUnitOfWork
        {
            public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            {
                fixture.Events.Add("save");
                return fixture.SaveException is { } error
                    ? Task.FromException<int>(error)
                    : Task.FromResult(1);
            }
        }
    }
}
