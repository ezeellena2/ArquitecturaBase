using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormSourceCompositionTests
{
    [Fact]
    public async Task Core_without_the_module_keeps_an_empty_cancellable_source()
    {
        using var provider = new ServiceCollection().AddApplication().BuildServiceProvider();
        var source = Assert.IsType<DisabledStructuredFormSource>(provider.GetRequiredService<IStructuredFormSource>());
        var page = await source.ListAsync(Guid.NewGuid(), "es", new() { Page = 2, PageSize = 7 }, TestContext.Current.CancellationToken);
        Assert.Empty(page.Items); Assert.Equal(0, page.TotalCount); Assert.Equal(2, page.Page); Assert.Equal(7, page.PageSize);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ListAsync(Guid.NewGuid(), "es", new ListStructuredFormsRequest(), cancelled.Token));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Optional_source_replaces_the_core_once_in_either_registration_order(bool coreFirst)
    {
        var services = new ServiceCollection();
        if (coreFirst) services.AddApplication().AddBusinessFormsApplication();
        else services.AddBusinessFormsApplication().AddApplication();
        services.AddBusinessFormsApplication();
        var registration = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IStructuredFormSource));
        Assert.Equal(typeof(BusinessFormSource), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }
}
