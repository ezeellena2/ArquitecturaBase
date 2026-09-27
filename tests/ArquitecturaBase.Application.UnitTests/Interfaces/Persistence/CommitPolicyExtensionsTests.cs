using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Interfaces.Persistence;

public sealed class CommitPolicyExtensionsTests
{
    private static readonly Error Failure = Error.Failure("Tests.Commit.Failed", "A business rule failed.");

    [Theory]
    [InlineData(CommitPolicy.OnSuccess, true, true)]
    [InlineData(CommitPolicy.OnSuccess, false, false)]
    [InlineData(CommitPolicy.OnAnyResult, true, true)]
    [InlineData(CommitPolicy.OnAnyResult, false, true)]
    public void Only_on_any_result_commits_a_failed_result(CommitPolicy policy, bool succeeded, bool commits)
    {
        var result = succeeded ? Result.Success() : Result.Failure(Failure);

        Assert.Equal(commits, policy.Commits(result));
    }

    [Fact]
    public void A_typed_result_follows_the_same_rule()
    {
        Assert.True(CommitPolicy.OnSuccess.Commits(Result.Success(42)));
        Assert.False(CommitPolicy.OnSuccess.Commits(Result.Failure<int>(Failure)));
        Assert.True(CommitPolicy.OnAnyResult.Commits(Result.Failure<int>(Failure)));
    }

    [Fact]
    public void A_missing_result_is_a_bug()
    {
        Assert.Throws<ArgumentNullException>(() => CommitPolicy.OnSuccess.Commits(null!));
    }

    [Fact]
    public void The_policies_are_exactly_on_success_and_on_any_result()
    {
        Assert.Equal(["OnSuccess", "OnAnyResult"], Enum.GetNames<CommitPolicy>());
    }

    /// <summary>
    /// Si se reordenaran los valores, una política sin inicializar confirmaría también los errores de negocio.
    /// </summary>
    [Fact]
    public void The_default_policy_is_the_conservative_one()
    {
        Assert.Equal(CommitPolicy.OnSuccess, default(CommitPolicy));
    }

    [Theory]
    [InlineData(CommitPolicy.OnSuccess)]
    [InlineData(CommitPolicy.OnAnyResult)]
    public void A_known_policy_passes_the_range_check(CommitPolicy policy)
    {
        policy.ThrowIfUndefined();
    }

    /// <summary>
    /// Fuera de rango, <see cref="CommitPolicyExtensions.Commits"/> se comportaría como OnSuccess sin avisar: la
    /// unidad de trabajo y sus dobles lo rechazan con el mismo chequeo antes de abrir nada.
    /// </summary>
    [Fact]
    public void An_undefined_policy_is_a_bug()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => ((CommitPolicy)7).ThrowIfUndefined());

        Assert.Equal("policy", error.ParamName);
        Assert.Equal((CommitPolicy)7, error.ActualValue);
    }
}
