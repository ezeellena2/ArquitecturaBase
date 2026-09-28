using ArquitecturaBase.Application.Interfaces.Integrations.Caching;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Application.Services.Settings;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.Validation.Settings;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Settings;

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
        Assert.Equal(
            ["Handling GetSystemSettings", "GetSystemSettings failed with Settings.System.NotFound"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
        Assert.Equal(LogLevel.Warning, fixture.Logger.Collector.GetSnapshot()[1].Level);
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
        Assert.Equal(
            ["Handling GetSystemSettings", "Handled GetSystemSettings"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
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
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
        Assert.Equal(
            ["Handling UpdateSystemSettings", "UpdateSystemSettings failed with " + SettingsErrors.NotFoundCode],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
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
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
        Assert.Equal(
            ["Handling UpdateSystemSettings", "UpdateSystemSettings failed with Validation.Failed"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
        Assert.Equal(LogLevel.Warning, fixture.Logger.Collector.GetSnapshot()[1].Level);
    }

    [Fact]
    public async Task Update_saves_the_changed_mode_once_before_invalidating_the_cache()
    {
        var fixture = new Fixture { Settings = SystemSettings.Create(RegistrationMode.InviteOnly) };

        var result = await fixture.Service.UpdateAsync(new UpdateSystemSettingsRequest(RegistrationMode.Open), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(RegistrationMode.Open, fixture.Settings.RegistrationMode);
        Assert.Equal(1, fixture.RepositoryGetCalls);
        Assert.Equal(["commit", "invalidate"], fixture.Events);
        Assert.Equal(CommitPolicy.OnSuccess, fixture.UnitOfWork.LastPolicy);
        Assert.Equal(
            ["Handling UpdateSystemSettings", "Handled UpdateSystemSettings"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    /// <summary>Un commit fallido no invalida el caché ni deja un Handled: solo queda el Handling.</summary>
    [Fact]
    public async Task Update_does_not_invalidate_the_cache_or_log_success_when_the_commit_fails()
    {
        var fixture = new Fixture
        {
            Settings = SystemSettings.Create(RegistrationMode.InviteOnly),
            SaveException = new InvalidOperationException("Commit failed."),
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.UpdateAsync(new UpdateSystemSettingsRequest(RegistrationMode.Open), Ct));

        Assert.Equal("Commit failed.", error.Message);
        Assert.Equal(["commit"], fixture.Events);
        Assert.Equal(1, fixture.UnitOfWork.Rollbacks);
        Assert.Equal(
            ["Handling UpdateSystemSettings"],
            fixture.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    private sealed class Fixture
    {
        private readonly FakeRepository _repository;
        private readonly FakeCache _cache;

        public Fixture()
        {
            _repository = new FakeRepository(this);
            _cache = new FakeCache(this);
            UnitOfWork = new FakeUnitOfWork(Events);
            Service = new SystemSettingsService(
                _repository,
                _cache,
                UnitOfWork,
                RequestValidators.For(new UpdateSystemSettingsRequestValidator()),
                Logger);
        }

        public FakeUnitOfWork UnitOfWork { get; }

        public SystemSettingsService Service { get; }

        public SystemSettings? Settings { get; set; }

        /// <summary>Hace fallar el commit, después de registrarlo en <see cref="Events"/>.</summary>
        public Exception? SaveException
        {
            get => UnitOfWork.CommitFailure;
            set => UnitOfWork.CommitFailure = value;
        }

        public FakeLogger<SystemSettingsService> Logger { get; } = new();

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

        private sealed class FakeCache(Fixture fixture) : ISystemSettingsCache
        {
            public Task InvalidateAsync(CancellationToken cancellationToken)
            {
                fixture.Events.Add("invalidate");
                return Task.CompletedTask;
            }
        }
    }
}
