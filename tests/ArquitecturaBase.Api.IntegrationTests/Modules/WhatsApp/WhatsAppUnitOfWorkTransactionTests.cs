using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// La parte del módulo WhatsApp de la unidad de trabajo contra Postgres de verdad (UnitOfWorkTransactionTests): los
/// locks de sus repositorios también exigen el límite, y el índice único de sus mensajes se traduce como cualquier
/// otro.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppUnitOfWorkTransactionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Locks_outside_the_boundary_throw_even_without_keys()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var userId = Guid.CreateVersion7();
        var contacts = services.GetRequiredService<IWhatsAppContactRepository>();

        Func<Task>[] locks =
        [
            () => services.GetRequiredService<IWhatsAppMessageRepository>().LockAsync([], Ct),
            () => contacts.LockAsync([], [], Ct),
            () => contacts.GetForProcessingAsync(Guid.CreateVersion7(), Ct),
            () => contacts.LockForNumberChangeAsync(userId, waId: null, Ct),
            () => contacts.GetByUserIdForUnlinkAsync(userId, Ct),
        ];

        foreach (var takeLock in locks)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(takeLock);
        }

        Assert.Null(services.GetRequiredService<ApplicationDbContext>().Database.CurrentTransaction);
    }

    [Fact]
    public async Task A_unique_violation_in_the_final_flush_is_translated_after_rolling_back()
    {
        var waMessageId = "wamid.uow-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        await factory.ExecuteScopeAsync(async services =>
        {
            services.GetRequiredService<IWhatsAppMessageRepository>().Add(Outbound(waMessageId));

            return await services.GetRequiredService<ApplicationDbContext>().SaveChangesAsync(Ct);
        });
        var widgetName = WidgetName();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var messages = scope.ServiceProvider.GetRequiredService<IWhatsAppMessageRepository>();

        var exception = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() =>
            unitOfWork.ExecuteInTransactionAsync(_ =>
            {
                messages.Add(Outbound(waMessageId));

                return Task.FromResult(Result.Success());
            }, CommitPolicy.OnSuccess, Ct));

        Assert.IsAssignableFrom<DbUpdateException>(exception.InnerException);
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Empty(db.ChangeTracker.Entries());

        // El mismo scope puede correr otro límite: nada de lo deshecho vuelve a bajar.
        await unitOfWork.ExecuteInTransactionAsync(_ =>
        {
            db.Set<Widget>().Add(new Widget(widgetName));

            return Task.FromResult(Result.Success());
        }, CommitPolicy.OnSuccess, Ct);
        Assert.True(await WidgetExistsAsync(widgetName));
    }

    private static string WidgetName() => "uow-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12];

    private WhatsAppMessage Outbound(string waMessageId) =>
        WhatsAppMessage.Outbound(contactId: null, waMessageId, WhatsAppMessageKind.Text, "Listo.", factory.Clock.GetUtcNow().UtcDateTime);

    private Task<bool> WidgetExistsAsync(string name) =>
        factory.ExecuteDbContextAsync(db => db.Set<Widget>().AnyAsync(widget => widget.Name == name, Ct));
}
