using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppFlowReservationBoundaryTests
{
    [Fact]
    public async Task Reserving_one_flow_operation_opens_one_transaction()
    {
        var unitOfWork = new FakeUnitOfWork();
        var service = new WhatsAppFlowDispatchService(null!, null!, null!, unitOfWork, null!,
            new FakeTimeProvider(), NullLogger<WhatsAppFlowDispatchService>.Instance);

        var result = await service.ReserveAsync(Guid.Empty, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(1, unitOfWork.Transactions);
    }
}
