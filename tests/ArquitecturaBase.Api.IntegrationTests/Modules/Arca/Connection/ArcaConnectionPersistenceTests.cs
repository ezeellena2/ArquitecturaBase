using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Connection;

[Collection(ApiTestGroup.Name)]
public sealed class ArcaConnectionPersistenceTests(ApiFactory factory)
{
    private static readonly DateTime NotBeforeUtc = new(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_installation_has_a_single_connection()
    {
        await ArcaPersistenceScenario.AddConnectionAsync(factory);
        try
        {
            await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => factory.InTransactionAsync(services =>
            {
                services.GetRequiredService<IArcaConnectionRepository>().Add(ArcaPersistenceScenario.NewConnection());
                return Task.CompletedTask;
            }));
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task A_connection_keeps_one_active_and_one_pending_certificate()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionId = await ArcaPersistenceScenario.AddConnectionAsync(factory);
        try
        {
            await factory.InTransactionAsync(services =>
            {
                var active = ArcaPersistenceScenario.NewCertificate(connectionId);
                active.Load("-----BEGIN CERTIFICATE-----", NotBeforeUtc, NotBeforeUtc.AddYears(2), "AB12");
                active.Promote();
                var repository = services.GetRequiredService<IArcaConnectionRepository>();
                repository.Add(active);
                repository.Add(ArcaPersistenceScenario.NewCertificate(connectionId, "renovacion"));
                return Task.CompletedTask;
            });

            await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => factory.InTransactionAsync(services =>
            {
                services.GetRequiredService<IArcaConnectionRepository>().Add(ArcaPersistenceScenario.NewCertificate(connectionId, "otro"));
                return Task.CompletedTask;
            }));

            var pending = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaConnectionRepository>()
                .GetCertificateAsync(connectionId, ArcaCertificateRole.Pending, ct));
            Assert.Equal("renovacion", pending?.Alias);
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task A_certificate_has_one_ticket_per_service_and_loses_them_with_the_connection()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionId = await ArcaPersistenceScenario.AddConnectionAsync(factory);
        var certificate = ArcaPersistenceScenario.NewCertificate(connectionId);
        try
        {
            await factory.InTransactionAsync(services =>
            {
                var repository = services.GetRequiredService<IArcaConnectionRepository>();
                repository.Add(certificate);
                repository.Add(ArcaAccessTicket.Create(certificate.Id, ArcaPersistenceScenario.Invoicing));
                repository.Add(ArcaAccessTicket.Create(certificate.Id, ArcaPersistenceScenario.Registry));
                return Task.CompletedTask;
            });

            await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => factory.InTransactionAsync(services =>
            {
                services.GetRequiredService<IArcaConnectionRepository>().Add(ArcaAccessTicket.Create(certificate.Id, ArcaPersistenceScenario.Invoicing));
                return Task.CompletedTask;
            }));
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }

        await factory.ExecuteScopeAsync(async services =>
        {
            var repository = services.GetRequiredService<IArcaConnectionRepository>();
            Assert.Null(await repository.GetCertificateAsync(connectionId, ArcaCertificateRole.Pending, ct));
            Assert.Null(await repository.GetTicketAsync(certificate.Id, ArcaPersistenceScenario.Invoicing, ct));
        });
    }

    [Fact]
    public async Task The_ticket_lease_and_its_protected_credentials_survive_a_new_scope()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionId = await ArcaPersistenceScenario.AddConnectionAsync(factory);
        var certificate = ArcaPersistenceScenario.NewCertificate(connectionId);
        var owner = Guid.NewGuid();
        var expiresAtUtc = NotBeforeUtc.AddHours(12);
        try
        {
            await factory.InTransactionAsync(services =>
            {
                var ticket = ArcaAccessTicket.Create(certificate.Id, ArcaPersistenceScenario.Invoicing);
                Assert.Equal(ArcaTicketLease.Acquired, ticket.TryLease(owner, NotBeforeUtc, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(1)));
                var repository = services.GetRequiredService<IArcaConnectionRepository>();
                repository.Add(certificate);
                repository.Add(ticket);
                return Task.CompletedTask;
            });

            await factory.InTransactionAsync(async services =>
            {
                var repository = services.GetRequiredService<IArcaConnectionRepository>();
                await repository.LockTicketAsync(certificate.Id, ArcaPersistenceScenario.Invoicing, ct);
                var ticket = await repository.GetTicketAsync(certificate.Id, ArcaPersistenceScenario.Invoicing, ct);
                Assert.True(ticket!.Store(owner, "protected-credentials", expiresAtUtc));
            });

            var row = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaConnectionReader>()
                .FindTicketAsync(certificate.Id, ArcaPersistenceScenario.Invoicing, ct));
            Assert.Equal(new ArcaTicketRow("protected-credentials", expiresAtUtc, null), row);
            Assert.DoesNotContain("protected-credentials", row!.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task The_reader_finds_the_pending_request_and_only_loaded_certificates_for_signing()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionId = await ArcaPersistenceScenario.AddConnectionAsync(factory);
        var active = ArcaPersistenceScenario.NewCertificate(connectionId);
        try
        {
            await factory.InTransactionAsync(services =>
            {
                active.Load("-----BEGIN CERTIFICATE-----", NotBeforeUtc, NotBeforeUtc.AddYears(2), "AB12");
                active.Promote();
                var repository = services.GetRequiredService<IArcaConnectionRepository>();
                repository.Add(active);
                repository.Add(ArcaPersistenceScenario.NewCertificate(connectionId, "renovacion"));
                return Task.CompletedTask;
            });

            await factory.ExecuteScopeAsync(async services =>
            {
                var reader = services.GetRequiredService<IArcaConnectionReader>();
                Assert.Equal(new ArcaPendingRequestRow("renovacion", "-----BEGIN CERTIFICATE REQUEST-----"), await reader.FindPendingRequestAsync(ct));
                Assert.Equal(new ArcaSigningCertificateRow(connectionId, active.Id, "20000000060", ArcaEnvironment.Testing, "protected-key",
                    "-----BEGIN CERTIFICATE-----"), await reader.FindSigningCertificateAsync(ArcaCertificateRole.Active, ct));
                Assert.Null(await reader.FindSigningCertificateAsync(ArcaCertificateRole.Pending, ct));
            });
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task The_reader_returns_an_untracked_row_without_the_private_key()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionId = await ArcaPersistenceScenario.AddConnectionAsync(factory);
        try
        {
            await factory.InTransactionAsync(services =>
            {
                services.GetRequiredService<IArcaConnectionRepository>().Add(ArcaPersistenceScenario.NewCertificate(connectionId));
                return Task.CompletedTask;
            });

            await factory.ExecuteScopeAsync(async services =>
            {
                var row = await services.GetRequiredService<IArcaConnectionReader>().FindAsync(ct);

                Assert.NotNull(row);
                Assert.Equal(connectionId, row.Id);
                Assert.Equal("20000000060", row.Cuit);
                Assert.Equal(ArcaConnectionStatus.AwaitingCertificate, row.Status);
                Assert.Equal(["ws_sr_constancia_inscripcion", "wsfe"], row.EnabledServices);
                var certificate = Assert.Single(row.Certificates);
                Assert.Equal(("plataforma", ArcaCertificateRole.Pending, false), (certificate.Alias, certificate.Role, certificate.IsLoaded));
                Assert.DoesNotContain(typeof(ArcaCertificateRow).GetProperties(), property => property.Name.Contains("Key", StringComparison.Ordinal));
                Assert.DoesNotContain("20000000060", row.ToString(), StringComparison.Ordinal);
                Assert.Empty(services.GetRequiredService<ApplicationDbContext>().ChangeTracker.Entries());
            });
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }

        Assert.Null(await factory.ExecuteScopeAsync(services => services.GetRequiredService<IArcaConnectionReader>().FindAsync(ct)));
    }
}
