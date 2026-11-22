using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// RC4-A and the documents' retention of RC1-A: Configuración › Formularios reads today's values until someone saves, and saves with the
// revision it read, under its lock and in one transaction.
public sealed class FormSettingsServiceTests
{
    [Fact]
    public async Task Settings_nobody_saved_read_today_values_at_revision_zero()
    {
        var fixture = new Fixture();
        var settings = (await fixture.Service.GetAsync(TestContext.Current.CancellationToken)).Value;
        Assert.Equal(new FormSettingsResponse(25, 5, 3, 90, 90, 0), settings);
    }

    [Fact]
    public async Task The_first_save_creates_the_settings_with_its_values_in_one_transaction_under_the_lock()
    {
        var fixture = new Fixture();
        var saved = await fixture.Service.UpdateAsync(new(0, 10, 7, 4, 30, 180), TestContext.Current.CancellationToken);
        Assert.Equal(new FormSettingsResponse(10, 7, 4, 30, 180, 1), saved.Value);
        Assert.NotNull(fixture.Stored);
        Assert.Equal((1, 1, 1, CommitPolicy.OnSuccess), (fixture.Locks, fixture.UnitOfWork.Transactions, fixture.UnitOfWork.Commits, fixture.UnitOfWork.LastPolicy));
        Assert.Equal(new FormSettingsResponse(10, 7, 4, 30, 180, 1), (await fixture.Service.GetAsync(TestContext.Current.CancellationToken)).Value);
    }

    [Fact]
    public async Task A_save_with_a_stale_revision_conflicts_without_changing_the_settings()
    {
        var fixture = new Fixture();
        Assert.True((await fixture.Service.UpdateAsync(new(0, 10, 7, 4, 30, 180), TestContext.Current.CancellationToken)).IsSuccess);
        var stale = await fixture.Service.UpdateAsync(new(0, 20, 7, 4, 30, 180), TestContext.Current.CancellationToken);
        Assert.Equal(FormSettingsErrors.RevisionConflict, stale.Error);
        Assert.Equal((10, 1L), (fixture.Stored!.MaxFileSizeMiB, fixture.Stored.Revision));
    }

    [Theory]
    [InlineData(26, 5, 3, 90, 90, "maxFileSizeMiB")]
    [InlineData(25, 0, 3, 90, 90, "dailyRereadsPerAsker")]
    [InlineData(25, 5, 21, 90, 90, "similarDocumentsToSuggest")]
    [InlineData(25, 5, 3, 3651, 90, "proposedExpiryDays")]
    [InlineData(25, 5, 3, 90, 0, "discardedRetentionDays")]
    public async Task A_value_out_of_its_range_is_refused_on_its_field_and_saves_nothing(int fileSize, int rereads, int similar, int expiry,
        int retention, string field)
    {
        var fixture = new Fixture();
        var result = await fixture.Service.UpdateAsync(new(0, fileSize, rereads, similar, expiry, retention), TestContext.Current.CancellationToken);
        Assert.Equal([field], Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        Assert.Null(fixture.Stored);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task The_range_message_names_the_bounds_in_the_numbers_of_the_language()
    {
        var result = await new Fixture().Service.UpdateAsync(new(0, 25, 5, 3, 3651, 90), TestContext.Current.CancellationToken);
        Assert.Contains("3", Assert.IsType<ValidationError>(result.Error).Errors["proposedExpiryDays"][0], StringComparison.Ordinal);
        Assert.Contains("650", Assert.IsType<ValidationError>(result.Error).Errors["proposedExpiryDays"][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_value_or_revision_is_refused_on_its_field()
    {
        var result = await new Fixture().Service.UpdateAsync(new(null, null, 5, 3, 90, 90), TestContext.Current.CancellationToken);
        Assert.Equal(["expectedRevision", "maxFileSizeMiB"], Assert.IsType<ValidationError>(result.Error).Errors.Keys.Order(StringComparer.Ordinal));
    }

    private sealed class Fixture : IFormSettingsReader, IFormSettingsRepository
    {
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public BusinessFormsSettings? Stored { get; private set; }
        public int Locks { get; private set; }
        public FormSettingsService Service { get; }
        public Fixture() => Service = new(this, this, UnitOfWork, RequestValidators.For(new UpdateFormSettingsRequestValidator()),
            NullLogger<FormSettingsService>.Instance);
        public Task<FormSettingsResponse> FindAsync(CancellationToken cancellationToken) => Task.FromResult(FormSettingsResponse.From(Stored ?? BusinessFormsSettings.Create()));
        public Task LockAsync(CancellationToken cancellationToken) { Locks++; return Task.CompletedTask; }
        public Task<BusinessFormsSettings?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Stored);
        public void Add(BusinessFormsSettings settings) => Stored = settings;
    }
}
