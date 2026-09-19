using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.UnitTests.Results;

public sealed class ResultTests
{
    private static readonly Error SampleError = Error.NotFound("Test.Sample.NotFound", "Sample not found.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Failure_carries_the_error()
    {
        var result = Result.Failure(SampleError);

        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
    }

    [Fact]
    public void Failure_without_error_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Result.Failure(Error.None));
    }

    [Fact]
    public void Success_with_value_exposes_the_value()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Value_of_failed_result_throws()
    {
        var result = Result.Failure<int>(SampleError);

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Value_converts_implicitly_to_successful_result()
    {
        Result<string> result = "hola";

        Assert.True(result.IsSuccess);
        Assert.Equal("hola", result.Value);
    }

    [Fact]
    public void Error_converts_implicitly_to_failed_result()
    {
        Result<string> typed = SampleError;
        Result untyped = SampleError;

        Assert.Equal(SampleError, typed.Error);
        Assert.Equal(SampleError, untyped.Error);
    }
}
