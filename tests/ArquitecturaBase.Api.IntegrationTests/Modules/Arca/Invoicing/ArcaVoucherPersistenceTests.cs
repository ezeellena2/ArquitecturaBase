using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Invoicing;

/// <summary>
/// La cola de emisión con PostgreSQL: la clave del pedido y el número de una secuencia no se repiten, la secuencia es una
/// por ambiente, CUIT, punto de venta y tipo, y los comprobantes no dependen de la conexión. Cada test usa su propia CUIT.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaVoucherPersistenceTests(ApiFactory factory)
{
    private static readonly DateOnly RequestedOn = new(2026, 11, 9);

    [Fact]
    public async Task A_request_is_queued_once()
    {
        var cuit = NewCuit();
        var requestKey = Guid.CreateVersion7();
        try
        {
            await AddAsync(Voucher(cuit, requestKey));

            await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => AddAsync(Voucher(cuit, requestKey)));
        }
        finally
        {
            await RemoveAsync(cuit);
        }
    }

    [Fact]
    public async Task Two_vouchers_of_a_sequence_never_hold_the_same_number()
    {
        var cuit = NewCuit();
        try
        {
            var first = Voucher(cuit);
            first.AssignNumber(12, "hash-1", Now);
            await AddAsync(first);
            var rejected = Voucher(cuit);
            rejected.AssignNumber(13, "hash-2", Now);
            rejected.Reject("wsfe.10015", null, Now);
            await AddAsync(rejected);

            var second = Voucher(cuit);
            second.AssignNumber(12, "hash-3", Now);
            await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => AddAsync(second));
        }
        finally
        {
            await RemoveAsync(cuit);
        }
    }

    [Fact]
    public async Task A_sequence_is_one_per_environment_cuit_point_of_sale_and_type()
    {
        var cuit = NewCuit();
        try
        {
            await AddAsync(ArcaVoucherSequence.Create(ArcaEnvironment.Testing, cuit, 4, 11));
            await AddAsync(ArcaVoucherSequence.Create(ArcaEnvironment.Production, cuit, 4, 11));
            await AddAsync(ArcaVoucherSequence.Create(ArcaEnvironment.Testing, cuit, 4, 13));

            await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => AddAsync(ArcaVoucherSequence.Create(ArcaEnvironment.Testing, cuit, 4, 11)));
        }
        finally
        {
            await RemoveAsync(cuit);
        }
    }

    [Fact]
    public async Task The_next_voucher_of_a_sequence_is_its_pending_one_or_else_the_oldest_unresolved()
    {
        var ct = TestContext.Current.CancellationToken;
        var cuit = NewCuit();
        try
        {
            var authorized = Voucher(cuit, createdAtUtc: Now.AddMinutes(-3));
            authorized.AssignNumber(10, "hash", Now);
            authorized.Authorize("76451234567890", RequestedOn.AddDays(10), null, Now);
            var oldest = Voucher(cuit, createdAtUtc: Now.AddMinutes(-2));
            var newest = Voucher(cuit, createdAtUtc: Now.AddMinutes(-1));
            var sequence = ArcaVoucherSequence.Create(ArcaEnvironment.Testing, cuit, 4, 11);
            await AddAsync(authorized, oldest, newest, sequence);

            var key = new ArcaVoucherSequenceRow(ArcaEnvironment.Testing, cuit, 4, 11);
            Assert.Equal(oldest.Id, await factory.InTransactionAsync(async services =>
            {
                var repository = services.GetRequiredService<IArcaVoucherRepository>();
                return (await repository.GetNextAsync((await repository.GetSequenceAsync(key, ct))!, ct))!.Id;
            }));

            await factory.InTransactionAsync(async services =>
                (await services.GetRequiredService<IArcaVoucherRepository>().GetSequenceAsync(key, ct))!.Hold(newest.Id));

            Assert.Equal(newest.Id, await factory.InTransactionAsync(async services =>
            {
                var repository = services.GetRequiredService<IArcaVoucherRepository>();
                return (await repository.GetNextAsync((await repository.GetSequenceAsync(key, ct))!, ct))!.Id;
            }));
        }
        finally
        {
            await RemoveAsync(cuit);
        }
    }

    [Fact]
    public async Task The_reader_lists_due_sequences_and_pdfs_and_reads_rows_by_request()
    {
        var ct = TestContext.Current.CancellationToken;
        var cuit = NewCuit();
        try
        {
            var due = Voucher(cuit);
            var waiting = Voucher(cuit, pointOfSale: 5);
            waiting.Postpone("Arca.Connection.Unavailable", Now);
            var authorized = Voucher(cuit, pointOfSale: 6);
            authorized.AssignNumber(1, "hash", Now);
            authorized.Authorize("76451234567890", RequestedOn.AddDays(10), null, Now);
            await AddAsync(due, waiting, authorized);

            var reader = factory.Services.CreateScope().ServiceProvider.GetRequiredService<IArcaVoucherReader>();
            var sequences = await reader.ListDueSequencesAsync(Now, 500, ct);
            Assert.Contains(new ArcaVoucherSequenceRow(ArcaEnvironment.Testing, cuit, 4, 11), sequences);
            Assert.DoesNotContain(sequences, key => key.Cuit == cuit && key.PointOfSale == 5);
            Assert.Contains(new ArcaVoucherSequenceRow(ArcaEnvironment.Testing, cuit, 5, 11), await reader.ListDueSequencesAsync(Now.AddSeconds(30), 500, ct));
            Assert.Contains(authorized.Id, await reader.ListDuePdfsAsync(Now, 500, ct));

            var row = Assert.Single(await reader.ListByRequestsAsync([authorized.RequestKey], ct));
            Assert.Equal((ArcaVoucherState.Authorized, (long?)1, "76451234567890", (DateOnly?)RequestedOn, 605500m),
                (row.State, row.Number, row.Cae, row.IssuedOn, row.Total));
        }
        finally
        {
            await RemoveAsync(cuit);
        }
    }

    [Fact]
    public async Task Disconnecting_keeps_the_vouchers()
    {
        var ct = TestContext.Current.CancellationToken;
        var cuit = NewCuit();
        var connectionId = await ArcaPersistenceScenario.AddConnectionAsync(factory);
        try
        {
            var voucher = Voucher(cuit, connectionId: connectionId);
            await AddAsync(voucher);

            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);

            Assert.Single(await factory.ExecuteScopeAsync(services =>
                services.GetRequiredService<IArcaVoucherReader>().ListByRequestsAsync([voucher.RequestKey], ct)));
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
            await RemoveAsync(cuit);
        }
    }

    private DateTime Now => factory.Clock.GetUtcNow().UtcDateTime;

    private ArcaVoucher Voucher(string cuit, Guid? requestKey = null, int pointOfSale = 4, DateTime? createdAtUtc = null, Guid? connectionId = null) =>
        ArcaVoucher.Create(connectionId ?? Guid.CreateVersion7(), ArcaEnvironment.Testing, requestKey ?? Guid.CreateVersion7(), ArcaVoucherClass.C,
            ArcaVoucherKind.Invoice, cuit, pointOfSale, 1, 80, 30712345671, RequestedOn, 605500m, "{}", null, createdAtUtc ?? Now);

    private Task AddAsync(params object[] items) => factory.InTransactionAsync(services =>
    {
        var repository = services.GetRequiredService<IArcaVoucherRepository>();
        foreach (var item in items)
        {
            if (item is ArcaVoucher voucher) repository.Add(voucher);
            else repository.Add((ArcaVoucherSequence)item);
        }

        return Task.CompletedTask;
    });

    // Sin clave foránea a la conexión, cada test borra lo suyo por su CUIT.
    private Task RemoveAsync(string cuit) => factory.ExecuteScopeAsync(async services =>
    {
        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        await dbContext.Set<ArcaVoucher>().Where(x => x.Cuit == cuit && x.CancelsVoucherId != null).ExecuteDeleteAsync();
        await dbContext.Set<ArcaVoucher>().Where(x => x.Cuit == cuit).ExecuteDeleteAsync();
        await dbContext.Set<ArcaVoucherSequence>().Where(x => x.Cuit == cuit).ExecuteDeleteAsync();
    });

    private static string NewCuit() => "2" + Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString(CultureInfo.InvariantCulture);
}
