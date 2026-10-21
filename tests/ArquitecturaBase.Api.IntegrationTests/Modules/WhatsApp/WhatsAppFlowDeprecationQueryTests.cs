using System.Reflection;
using ArquitecturaBase.Application;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppFlowDeprecationQueryTests
{
    [Fact]
    public void Due_bindings_translate_version_work_and_fenced_scheduling_without_requiring_a_successful_whole_publication()
    {
        using var services = Services(); using var scope = services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IWhatsAppFlowReader>();
        var method = reader.GetType().GetMethod("DueDeprecationRows", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var sql = Assert.IsAssignableFrom<IQueryable>(method.Invoke(reader, [Now])).ToQueryString();
        foreach (var name in new[] { "VersionId", "DeprecationState", "DeprecationLeaseExpiresAtUtc", "DeprecationNextCheckAtUtc",
                     "CaptureState", "FlowSessionId", "OccurredAtUtc", "ProcessedAtUtc", "ProtectedFlowResponse" })
            Assert.Contains("\"" + name + "\"", sql, StringComparison.Ordinal);
        Assert.Contains("'Published'", sql, StringComparison.Ordinal);
        Assert.Contains("'Deprecated'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Completed\"", sql, StringComparison.Ordinal);
    }
    [Fact]
    public void Visible_channel_state_counts_published_bindings_even_without_sessions_and_dates_only_confirmed_retirement()
    {
        using var services = Services(); using var scope = services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IStructuredFormChannelStateSource>();
        var method = reader.GetType().GetMethod("Rows", BindingFlags.NonPublic | BindingFlags.Instance); Assert.NotNull(method);
        var sql = Assert.IsAssignableFrom<IQueryable>(method.Invoke(reader, [new[] { Guid.NewGuid() }, Now])).ToQueryString();
        foreach (var name in new[] { "WhatsAppFlowBindings", "DeprecationState", "DeprecatedAtUtc", "DefinitionId" })
            Assert.Contains("\"" + name + "\"", sql, StringComparison.Ordinal);
        Assert.Contains("'Published'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Completed\"", sql, StringComparison.Ordinal);
    }
    [Fact]
    public void Retirement_fencing_and_due_lookup_are_separate_from_the_sealed_publication_state()
    {
        var model = new ModelBuilder(); model.ApplyConfiguration(new WhatsAppFlowBindingConfiguration());
        var entity = model.Model.FindEntityType(typeof(WhatsAppFlowBinding))!;
        Assert.True(entity.FindProperty(nameof(WhatsAppFlowBinding.DeprecationFence))!.IsConcurrencyToken);
        Assert.Equal(typeof(string), entity.FindProperty(nameof(WhatsAppFlowBinding.DeprecationState))!.GetProviderClrType());
        var index = Assert.Single(entity.GetIndexes(), item => item.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(WhatsAppFlowBinding.DeprecationNextCheckAtUtc), nameof(WhatsAppFlowBinding.Id)]));
        Assert.Equal("\"State\" = 'Published' AND \"DeprecationState\" <> 'Deprecated'", index.GetFilter());
    }
    private static DateTime Now => new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    private static ServiceProvider Services()
    {
        var services = new ServiceCollection(); services.AddApplication();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test"));
        services.AddWhatsAppInfrastructure(new ConfigurationBuilder().Build()); return services.BuildServiceProvider();
    }
}
