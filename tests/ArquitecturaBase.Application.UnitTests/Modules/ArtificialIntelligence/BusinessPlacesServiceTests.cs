using ArquitecturaBase.Application.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;
public sealed class BusinessPlacesServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("invalid\nquery")]
    [InlineData(null)]
    public async Task Invalid_input_never_reaches_the_external_provider(string? input)
    {
        var client = new Client();
        using var provider = new ServiceCollection().AddLogging().AddApplication().AddArtificialIntelligenceApplication()
            .AddSingleton<IBusinessPlacesClient>(client).BuildServiceProvider();
        var result = await provider.GetRequiredService<IBusinessPlacesService>().SearchAsync(new(input!), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(0, client.Calls);
    }
    [Fact]
    public async Task Search_trims_input_and_requires_neither_persistence_nor_a_connection_credential()
    {
        var client = new Client();
        using var provider = new ServiceCollection().AddLogging().AddApplication().AddArtificialIntelligenceApplication()
            .AddSingleton<IBusinessPlacesClient>(client).BuildServiceProvider();
        var result = await provider.GetRequiredService<IBusinessPlacesService>().SearchAsync(new("  Example clinic  "), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal("Example clinic", client.Input);
        Assert.Equal(1, client.Calls);
    }
    private sealed class Client : IBusinessPlacesClient
    {
        public int Calls { get; private set; }
        public string? Input { get; private set; }
        public Task<Result<BusinessPlacesResponse>> SearchAsync(string input, string languageCode, CancellationToken cancellationToken)
        {
            Calls++; Input = input;
            return Task.FromResult(Result.Success(new BusinessPlacesResponse([])));
        }
    }
}
