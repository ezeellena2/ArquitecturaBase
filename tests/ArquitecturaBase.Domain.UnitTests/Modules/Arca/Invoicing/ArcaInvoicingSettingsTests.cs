using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaInvoicingSettingsTests
{
    [Fact]
    public void The_settings_are_complete_only_with_a_point_of_sale()
    {
        var settings = ArcaInvoicingSettings.Create(Guid.CreateVersion7());
        Assert.False(settings.IsComplete);

        Assert.True(settings.ChoosePointOfSale(5).IsSuccess);

        Assert.True(settings.IsComplete);
        Assert.Equal(5, settings.PointOfSale);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99999)]
    public void A_point_of_sale_goes_from_1_to_99998(int number)
    {
        var settings = ArcaInvoicingSettings.Create(Guid.CreateVersion7());

        Assert.Equal(ArcaInvoicingErrors.PointOfSaleInvalid, settings.ChoosePointOfSale(number).Error);
        Assert.Null(settings.PointOfSale);
    }

    [Fact]
    public void The_edges_are_valid()
    {
        var settings = ArcaInvoicingSettings.Create(Guid.CreateVersion7());
        Assert.True(settings.ChoosePointOfSale(1).IsSuccess);
        Assert.True(settings.ChoosePointOfSale(99998).IsSuccess);
    }
}
