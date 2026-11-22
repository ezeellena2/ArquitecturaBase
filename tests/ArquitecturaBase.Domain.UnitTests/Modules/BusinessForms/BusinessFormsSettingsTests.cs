using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

// RC4-A and the documents' retention of RC1-A: Configuración › Formularios, with today's values until someone saves others.
public sealed class BusinessFormsSettingsTests
{
    [Fact]
    public void Settings_nobody_saved_have_today_values_and_revision_zero()
    {
        var settings = BusinessFormsSettings.Create();
        Assert.Equal(BusinessFormsSettings.SingletonId, settings.Id);
        Assert.Equal((25, 5, 3, 90, 90, 0L), (settings.MaxFileSizeMiB, settings.DailyRereadsPerAsker, settings.SimilarDocumentsToSuggest,
            settings.ProposedExpiryDays, settings.DiscardedRetentionDays, settings.Revision));
    }

    [Fact]
    public void A_save_with_the_revision_it_read_keeps_every_value_and_advances_the_revision()
    {
        var settings = BusinessFormsSettings.Create();
        Assert.True(settings.Update(0, 10, 7, 4, 30, 180).IsSuccess);
        Assert.Equal((10, 7, 4, 30, 180, 1L), (settings.MaxFileSizeMiB, settings.DailyRereadsPerAsker, settings.SimilarDocumentsToSuggest,
            settings.ProposedExpiryDays, settings.DiscardedRetentionDays, settings.Revision));
    }

    [Fact]
    public void A_save_with_a_stale_revision_conflicts_and_changes_nothing()
    {
        var settings = BusinessFormsSettings.Create();
        Assert.True(settings.Update(0, 10, 7, 4, 30, 180).IsSuccess);
        Assert.Equal(FormSettingsErrors.RevisionConflict, settings.Update(0, 20, 7, 4, 30, 180).Error);
        Assert.Equal((10, 1L), (settings.MaxFileSizeMiB, settings.Revision));
    }

    [Theory]
    [InlineData(0, 5, 3, 90, 90)]
    [InlineData(26, 5, 3, 90, 90)]
    [InlineData(25, 0, 3, 90, 90)]
    [InlineData(25, 101, 3, 90, 90)]
    [InlineData(25, 5, 1, 90, 90)]
    [InlineData(25, 5, 21, 90, 90)]
    [InlineData(25, 5, 3, 0, 90)]
    [InlineData(25, 5, 3, 3651, 90)]
    [InlineData(25, 5, 3, 90, 0)]
    [InlineData(25, 5, 3, 90, 3651)]
    public void A_value_out_of_its_range_is_refused_and_changes_nothing(int fileSize, int rereads, int similar, int expiry, int retention)
    {
        var settings = BusinessFormsSettings.Create();
        Assert.Equal(FormSettingsErrors.ContentInvalid, settings.Update(0, fileSize, rereads, similar, expiry, retention).Error);
        Assert.Equal((25, 0L), (settings.MaxFileSizeMiB, settings.Revision));
    }

    [Theory]
    [InlineData(1, 1, 2, 1, 1)]
    [InlineData(25, 100, 20, 3650, 3650)]
    public void The_bounds_of_each_range_are_accepted(int fileSize, int rereads, int similar, int expiry, int retention) =>
        Assert.True(BusinessFormsSettings.Create().Update(0, fileSize, rereads, similar, expiry, retention).IsSuccess);
}
