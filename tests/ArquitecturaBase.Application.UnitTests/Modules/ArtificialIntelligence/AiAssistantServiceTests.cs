using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Invalid_questions_do_not_open_a_transaction(string question)
    {
        var work = new FakeUnitOfWork();
        var validator = RequestValidators.For(new SubmitAiAssistantTurnRequestValidator(new AiAssistantLimits()));
        var service = new AiAssistantService(null!, work, validator, null!, null!, null!, new FakeTimeProvider(),
            NullLogger<AiAssistantService>.Instance);
        var result = await service.SubmitAsync(new(Guid.NewGuid(), null, null, 1, question, "es"), TestContext.Current.CancellationToken);
        Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(0, work.Transactions);
    }

    [Fact]
    public async Task Unicode_length_and_conversation_revision_are_validated_before_writing()
    {
        var work = new FakeUnitOfWork();
        var validator = RequestValidators.For(new SubmitAiAssistantTurnRequestValidator(new AiAssistantLimits()));
        var service = new AiAssistantService(null!, work, validator, null!, null!, null!, new FakeTimeProvider(),
            NullLogger<AiAssistantService>.Instance);
        var question = string.Concat(Enumerable.Repeat("😀", 2001));
        var result = await service.SubmitAsync(new(Guid.NewGuid(), Guid.NewGuid(), null, 1, question, "es"), TestContext.Current.CancellationToken);
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("question", error.Errors.Keys);
        Assert.Contains("expectedRevision", error.Errors.Keys);
        Assert.Equal(0, work.Transactions);
    }
}
