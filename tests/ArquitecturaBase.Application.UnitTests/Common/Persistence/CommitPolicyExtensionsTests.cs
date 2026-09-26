using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Common.Persistence;

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
}
