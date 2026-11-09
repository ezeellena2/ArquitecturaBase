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
    public void The_voucher_data_keeps_the_gross_income_trimmed_and_the_activity_start()
    {
        var settings = ArcaInvoicingSettings.Create(Guid.CreateVersion7());
        Assert.False(settings.HasVoucherData);

        Assert.True(settings.SetVoucherData(" 27-28765433-8 ", new DateOnly(2019, 3, 1)).IsSuccess);

        Assert.Equal(("27-28765433-8", (DateOnly?)new DateOnly(2019, 3, 1)), (settings.GrossIncomeNumber, settings.ActivityStartOn));
        Assert.True(settings.HasVoucherData);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("1234567890123456789012345678901")]
    public void A_gross_income_number_is_required_and_short(string? number)
    {
        var settings = ArcaInvoicingSettings.Create(Guid.CreateVersion7());

        Assert.Equal(ArcaInvoicingErrors.GrossIncomeInvalid, settings.SetVoucherData(number, new DateOnly(2019, 3, 1)).Error);
        Assert.Null(settings.GrossIncomeNumber);
    }

    [Fact]
    public void The_activity_start_is_required()
    {
        Assert.Equal(ArcaInvoicingErrors.ActivityStartInvalid, ArcaInvoicingSettings.Create(Guid.CreateVersion7()).SetVoucherData("123", default).Error);
    }

    [Fact]
    public void The_edges_are_valid()
    {
        var settings = ArcaInvoicingSettings.Create(Guid.CreateVersion7());
        Assert.True(settings.ChoosePointOfSale(1).IsSuccess);
        Assert.True(settings.ChoosePointOfSale(99998).IsSuccess);
    }
}
