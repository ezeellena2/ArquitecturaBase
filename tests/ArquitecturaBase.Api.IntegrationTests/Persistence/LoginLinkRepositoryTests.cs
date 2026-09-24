using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

[Collection(ApiTestGroup.Name)]
public sealed class LoginLinkRepositoryTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pending_links_include_expired_ones_and_remain_tracked_for_invalidation()
    {
        var accountId = await CreateAccountAsync();
        var otherAccountId = await CreateAccountAsync();
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        var active = Issue(accountId, nowUtc);
        var expired = Issue(accountId, nowUtc - LoginLink.Lifetime - TimeSpan.FromSeconds(1));
        var consumed = Issue(accountId, nowUtc);
        consumed.Redeem(nowUtc);
        var invalidated = Issue(accountId, nowUtc);
        invalidated.Invalidate(nowUtc);
        var anotherAccount = Issue(otherAccountId, nowUtc);

        await factory.ExecuteDbContextAsync(async db =>
        {
            db.LoginLinks.AddRange(active, expired, consumed, invalidated, anotherAccount);

            return await db.SaveChangesAsync(Ct);
        });

        await factory.ExecuteDbContextAsync(async db =>
        {
            var pending = await new LoginLinkRepository(db).ListPendingAsync(accountId, Ct);

            Assert.Equal(2, pending.Count);
            Assert.Contains(pending, link => link.Id == active.Id);
            Assert.Contains(pending, link => link.Id == expired.Id);
            Assert.All(pending, link => Assert.Equal(EntityState.Unchanged, db.Entry(link).State));

            foreach (var link in pending)
            {
                link.Invalidate(nowUtc);
            }

            return await db.SaveChangesAsync(Ct);
        });

        var persisted = await factory.ExecuteDbContextAsync(db => db.LoginLinks
            .AsNoTracking()
            .Where(link => link.UserId == accountId)
            .ToDictionaryAsync(link => link.Id, link => link.InvalidatedAtUtc, Ct));
        Assert.Equal(nowUtc, persisted[active.Id]);
        Assert.Equal(nowUtc, persisted[expired.Id]);
        Assert.Null(persisted[consumed.Id]);
        Assert.Equal(nowUtc, persisted[invalidated.Id]);
    }

    private async Task<Guid> CreateAccountAsync() =>
        (await factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>().CreateAsync(
            Email.Create(TestEmails.Unique("pendinglinks")).Value,
            phone: null,
            phoneConfirmed: false,
            displayName: null,
            culture: "es",
            Ct))).Id;

    private static LoginLink Issue(Guid userId, DateTime createdAtUtc) =>
        LoginLink.Issue(userId, Guid.NewGuid().ToString("N"), createdAtUtc);
}
