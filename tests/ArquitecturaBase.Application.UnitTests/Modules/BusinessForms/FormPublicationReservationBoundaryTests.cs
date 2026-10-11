using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormPublicationReservationBoundaryTests
{
    [Fact]
    public async Task Reserving_one_publication_opens_one_transaction()
    {
        var unitOfWork = new FakeUnitOfWork();
        var service = new FormPublicationDispatchService(null!, null!, null!, null!, unitOfWork, null!,
            new FakeTimeProvider(), NullLogger<FormPublicationDispatchService>.Instance);

        var result = await service.ReserveAsync(Guid.Empty, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(1, unitOfWork.Transactions);
    }
}
