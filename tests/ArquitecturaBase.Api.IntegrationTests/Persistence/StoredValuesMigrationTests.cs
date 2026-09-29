using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// Las migraciones que cambian valores ya guardados, contra una base nueva: el canal de los códigos por teléfono pasó de
/// 'WhatsApp' a 'Phone' (LoginCodePhoneChannel, una migración de datos escrita a mano adentro de la generada vacía) y la
/// columna del id del proveedor de las invitaciones, de WaMessageId a ProviderMessageId (un RenameColumn: un DropColumn
/// perdería los ids de Meta de las invitaciones ya mandadas). Las filas se escriben con SQL antes de migrar, como las
/// dejó la versión anterior, y los valores tienen que volver al deshacerlas.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class StoredValuesMigrationTests(ApiFactory factory)
{
    /// <summary>La última migración antes de las que cambian valores guardados.</summary>
    private const string Before = "20260924035855_UserInvitations";

    private static readonly DateTime CreatedAt = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_phone_channel_and_the_provider_message_id_keep_the_stored_values_both_ways()
    {
        await using var database = factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("storedvalues")));
        await using var scope = database.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = dbContext.GetService<IMigrator>();
        var (phoneCode, emailCode, invitation) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        try
        {
            await migrator.MigrateAsync(Before, Ct);
            await InsertCodeAsync(dbContext, phoneCode, "WhatsApp", "+5493515550101");
            await InsertCodeAsync(dbContext, emailCode, "Email", "ana@example.test");
            await dbContext.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "UserInvitations"
                    ("Id", "UserId", "Channel", "SentBy", "SentAtUtc", "ConsentConfirmedBy", "ConsentConfirmedAtUtc", "SendFailed", "WaMessageId")
                VALUES ({invitation}, {Guid.CreateVersion7()}, 'WhatsApp', {Guid.CreateVersion7()}, {CreatedAt}, NULL, NULL, FALSE, 'wamid.x')
                """,
                Ct);

            await migrator.MigrateAsync(cancellationToken: Ct);

            Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync(Ct));
            Assert.Equal(LoginCodeChannel.Phone, (await dbContext.LoginCodes.AsNoTracking().SingleAsync(code => code.Id == phoneCode, Ct)).Channel);
            Assert.Equal(LoginCodeChannel.Email, (await dbContext.LoginCodes.AsNoTracking().SingleAsync(code => code.Id == emailCode, Ct)).Channel);
            var migrated = await dbContext.UserInvitations.AsNoTracking().SingleAsync(row => row.Id == invitation, Ct);
            Assert.Equal("wamid.x", migrated.ProviderMessageId);
            // El canal de la invitación es otro enum, que no cambió: sigue siendo WhatsApp.
            Assert.Equal(UserInvitationChannel.WhatsApp, migrated.Channel);

            await migrator.MigrateAsync(Before, Ct);

            Assert.Equal("WhatsApp", await ScalarAsync(dbContext, $"""SELECT "Channel" AS "Value" FROM "LoginCodes" WHERE "Id" = {phoneCode}"""));
            Assert.Equal("Email", await ScalarAsync(dbContext, $"""SELECT "Channel" AS "Value" FROM "LoginCodes" WHERE "Id" = {emailCode}"""));
            Assert.Equal("wamid.x", await ScalarAsync(dbContext, $"""SELECT "WaMessageId" AS "Value" FROM "UserInvitations" WHERE "Id" = {invitation}"""));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync(Ct);
        }
    }

    private static Task<int> InsertCodeAsync(ApplicationDbContext dbContext, Guid id, string channel, string destination) =>
        dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "LoginCodes"
                ("Id", "Channel", "Destination", "Purpose", "CodeHash", "CreatedAtUtc", "ExpiresAtUtc", "FailedAttempts", "MaxAttempts")
            VALUES ({id}, {channel}, {destination}, 'SignIn', 'hash', {CreatedAt}, {CreatedAt.AddMinutes(10)}, 0, 5)
            """,
            Ct);

    private static Task<string> ScalarAsync(ApplicationDbContext dbContext, FormattableString sql) =>
        dbContext.Database.SqlQuery<string>(sql).SingleAsync(Ct);
}
