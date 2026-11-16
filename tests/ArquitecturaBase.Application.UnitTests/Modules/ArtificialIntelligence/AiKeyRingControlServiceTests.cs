using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// I17: each key of the ring has a control value in the database; a copy whose value differs is reported and writes nothing.
public sealed class AiKeyRingControlServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_copy_writes_the_control_of_each_key_of_its_ring_under_the_lock()
    {
        using var host = new Host(new() { ["one"] = "control-one", ["two"] = "control-two" });
        var checkedRing = await host.Service.CheckAsync(Ct);
        Assert.Empty(checkedRing.Value);
        Assert.Equal([("one", "control-one"), ("two", "control-two")],
            host.Store.Controls.OrderBy(control => control.KeyId, StringComparer.Ordinal).Select(control => (control.KeyId, control.Control)));
        Assert.True(host.Store.LockedInTransaction);
        Assert.Equal(1, host.Work.Commits);
    }

    [Fact]
    public async Task A_copy_with_the_same_ring_matches_and_writes_nothing()
    {
        using var host = new Host(new() { ["one"] = "control-one" });
        host.Store.Controls.Add(AiKeyRingControl.Create("one", "control-one", Now));
        Assert.Empty((await host.Service.CheckAsync(Ct)).Value);
        Assert.Single(host.Store.Controls);
    }

    [Fact]
    public async Task A_key_whose_control_differs_is_reported_and_the_copy_writes_no_other_key()
    {
        using var host = new Host(new() { ["one"] = "another-control", ["three"] = "control-three" });
        host.Store.Controls.Add(AiKeyRingControl.Create("one", "control-one", Now));
        Assert.Equal(["one"], (await host.Service.CheckAsync(Ct)).Value);
        Assert.Equal("control-one", Assert.Single(host.Store.Controls).Control);
    }

    [Fact]
    public async Task A_key_retired_from_the_ring_is_not_compared()
    {
        using var host = new Host(new() { ["new"] = "control-new" });
        host.Store.Controls.Add(AiKeyRingControl.Create("old", "control-old", Now));
        Assert.Empty((await host.Service.CheckAsync(Ct)).Value);
        Assert.Equal(["new", "old"], host.Store.Controls.Select(control => control.KeyId).Order(StringComparer.Ordinal));
    }

    private static DateTime Now => new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Host : IDisposable
    {
        public FakeUnitOfWork Work { get; } = new();
        public Store Store { get; } = new();
        private ServiceProvider Provider { get; }
        public IAiKeyRingControlService Service => Provider.GetRequiredService<IAiKeyRingControlService>();
        public Host(Dictionary<string, string> ring)
        {
            var services = new ServiceCollection().AddLogging().AddApplication().AddArtificialIntelligenceApplication();
            services.AddSingleton<IUnitOfWork>(Work);
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
            services.AddSingleton<IAiKeyRingControlRepository>(Store);
            services.AddSingleton<IAiKeyRingControl>(new Ring(ring));
            Store.InTransaction = () => Work.InTransaction;
            Provider = services.BuildServiceProvider();
        }
        public void Dispose() => Provider.Dispose();
    }

    private sealed class Ring(Dictionary<string, string> controls) : IAiKeyRingControl
    {
        public IReadOnlyDictionary<string, string> CreateControls() => controls;
    }

    private sealed class Store : IAiKeyRingControlRepository
    {
        public List<AiKeyRingControl> Controls { get; } = [];
        public Func<bool> InTransaction { get; set; } = () => false;
        public bool LockedInTransaction { get; private set; }
        public Task LockAsync(CancellationToken cancellationToken)
        {
            LockedInTransaction = InTransaction();
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<AiKeyRingControl>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AiKeyRingControl>>([.. Controls]);
        public void Add(AiKeyRingControl control) => Controls.Add(control);
    }
}
