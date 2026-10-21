using System.Reflection;
using ArquitecturaBase.Application;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppFormChannelStateQueryTests
{
    [Fact]
    public void The_local_channel_state_reader_remains_scoped_when_the_transport_is_disabled()
    {
        using var services = Services();
        using var first = services.CreateScope();
        using var second = services.CreateScope();
        var reader = first.ServiceProvider.GetRequiredService<IStructuredFormChannelStateSource>();
        Assert.Equal("WhatsAppFormChannelStateReader", reader.GetType().Name);
        Assert.Same(reader, first.ServiceProvider.GetRequiredService<IStructuredFormChannelStateSource>());
        Assert.NotSame(reader, second.ServiceProvider.GetRequiredService<IStructuredFormChannelStateSource>());
    }

    [Fact]
    public void Channel_state_translates_the_requested_batch_and_durable_reply_guards_to_SQL()
    {
        using var services = Services();
        using var scope = services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IStructuredFormChannelStateSource>();
        var method = reader.GetType().GetMethod("Rows", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(reader, [new[] { Guid.NewGuid(), Guid.NewGuid() }, now]));
        var sql = query.ToQueryString();
        Assert.Contains("MAX(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        foreach (var column in new[] { "DefinitionId", "CaptureState", "ExpiresAtUtc", "FlowSessionId", "ContactId",
                     "Direction", "Kind", "ProcessedAtUtc", "ProtectedFlowResponse", "OccurredAtUtc", "CreatedAtUtc" })
            Assert.Contains("\"" + column + "\"", sql, StringComparison.Ordinal);
        Assert.Contains("'Pending'", sql, StringComparison.Ordinal);
        Assert.Contains("'Inbound'", sql, StringComparison.Ordinal);
        Assert.Contains("'FlowReply'", sql, StringComparison.Ordinal);
        Assert.Contains("IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IS NOT NULL", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Pending_sessions_have_a_version_and_expiry_index_for_retirement_checks()
    {
        var model = new ModelBuilder();
        model.ApplyConfiguration(new WhatsAppFlowSessionConfiguration());
        var entity = model.Model.FindEntityType(typeof(WhatsAppFlowSession))!;
        var index = Assert.Single(entity.GetIndexes(), item => item.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(WhatsAppFlowSession.VersionId), nameof(WhatsAppFlowSession.ExpiresAtUtc)]));
        Assert.Equal("\"CaptureState\" = 'Pending'", index.GetFilter());
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test"));
        services.AddWhatsAppInfrastructure(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }
}
